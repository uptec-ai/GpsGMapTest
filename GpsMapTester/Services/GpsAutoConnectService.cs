using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GpsMapTester.Helpers;
using GpsMapTester.Models;

namespace GpsMapTester.Services
{
    /// <summary>GPS 연결 상태</summary>
    public enum GpsConnectionState
    {
        /// <summary>연결 안 됨</summary>
        Disconnected,
        /// <summary>포트를 탐색/연결 시도 중</summary>
        Searching,
        /// <summary>연결됨 (유효 NMEA 수신 중)</summary>
        Connected
    }

    /// <summary>
    /// BU-353N 자동 연결 서비스.
    /// - 앱 시작과 동시에 백그라운드에서 COM 포트를 스캔하고 baud rate 를 자동 감지합니다.
    /// - 유효한 NMEA 문장이 수신되는 포트를 찾으면 연결을 유지합니다.
    /// - 연결이 끊기거나 데이터가 멈추면 다시 탐색/재연결을 반복합니다.
    ///
    /// 주의: 이벤트(StateChanged/DataUpdated)는 UI 스레드가 아닌 백그라운드 스레드에서 발생합니다.
    ///       구독측(뷰모델)에서 Dispatcher 로 마샬링하세요.
    /// </summary>
    public class GpsAutoConnectService : IDisposable
    {
        private readonly GpsService _gps = new GpsService();
        private readonly NmeaParser _parser = new NmeaParser();
        private readonly int[] _baudCandidates;
        private readonly object _sync = new object();

        private CancellationTokenSource _cts;
        private Task _worker;
        private volatile bool _sentenceSeen;
        private DateTime _lastDataUtc = DateTime.MinValue;
        private GpsConnectionState _reportedState = (GpsConnectionState)(-1);
        private bool _disposed;

        // ─── 튜닝 상수 ────────────────────────────────────────────────────
        /// <summary>포트 하나에서 유효 NMEA 를 기다리는 최대 시간(ms)</summary>
        private const int ProbeTimeoutMs = 2500;
        /// <summary>연결 후 데이터가 이 시간(초) 이상 끊기면 연결 유실로 간주</summary>
        private const int DataTimeoutSec = 5;

        // ─── 이벤트 ───────────────────────────────────────────────────────
        /// <summary>연결 상태 변경 (state, 사람이 읽는 상세 메시지)</summary>
        public event Action<GpsConnectionState, string> StateChanged;

        /// <summary>유효 NMEA 파싱 후 최신 GPS 데이터</summary>
        public event Action<GpsData> DataUpdated;

        /// <summary>진단용 로그(선택)</summary>
        public event Action<string> Log;

        // ─── 상태 ─────────────────────────────────────────────────────────
        public GpsConnectionState State { get; private set; } = GpsConnectionState.Disconnected;
        public string ConnectedPort { get; private set; }
        public int ConnectedBaud { get; private set; }

        /// <param name="baudCandidates">
        /// 자동 감지 시 시도할 baud rate 목록. 기본값은 BU-353N(u-blox, 9600) 우선.
        /// </param>
        public GpsAutoConnectService(int[] baudCandidates = null)
        {
            _baudCandidates = (baudCandidates != null && baudCandidates.Length > 0)
                ? baudCandidates
                : new[] { 9600, 4800, 115200, 38400 };

            _gps.SentenceReceived += OnSentence;
            _gps.ErrorOccurred    += OnError;
        }

        // ─── 시작 / 중지 ──────────────────────────────────────────────────
        /// <summary>백그라운드 탐색/연결 루프를 시작합니다. (중복 호출 안전)</summary>
        public void Start()
        {
            if (_worker != null) return;
            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => RunLoopAsync(_cts.Token));
        }

        /// <summary>루프를 중지하고 연결을 해제합니다.</summary>
        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _worker?.Wait(1000); } catch { }
            SafeDisconnect();
            _worker = null;
        }

        // ─── 수신 처리 (SerialPort 스레드) ────────────────────────────────
        private void OnSentence(string sentence)
        {
            _sentenceSeen = true;
            GpsData snapshot;
            lock (_sync)
            {
                _lastDataUtc = DateTime.UtcNow;
                _parser.Parse(sentence);
                snapshot = _parser.CurrentData;
            }
            DataUpdated?.Invoke(snapshot);
        }

        private void OnError(Exception ex)
        {
            Log?.Invoke("포트 오류: " + ex.Message);
            // 실제 연결 유실 판정은 RunLoop 의 freshness 체크에서 처리
        }

        // ─── 메인 루프 (백그라운드 Task) ──────────────────────────────────
        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!_gps.IsConnected)
                    {
                        SetState(GpsConnectionState.Searching, "GPS 장치 탐색 중...");
                        await TryConnectAnyPortAsync(ct).ConfigureAwait(false);
                        await Task.Delay(_gps.IsConnected ? 500 : 1500, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        DateTime last;
                        lock (_sync) last = _lastDataUtc;

                        if (last != DateTime.MinValue &&
                            (DateTime.UtcNow - last).TotalSeconds > DataTimeoutSec)
                        {
                            Log?.Invoke("데이터 수신 중단 → 재연결");
                            SafeDisconnect();
                            SetState(GpsConnectionState.Disconnected, "연결 끊김 · 재탐색");
                            continue;
                        }
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (Exception ex) { Log?.Invoke("루프 오류: " + ex.Message); }
        }

        /// <summary>사용 가능한 모든 포트를 순회하며 유효 NMEA 포트를 탐색합니다.</summary>
        private async Task TryConnectAnyPortAsync(CancellationToken ct)
        {
            string[] ports;
            try { ports = SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToArray(); }
            catch { return; }

            if (ports.Length == 0)
            {
                SetState(GpsConnectionState.Searching, "COM 포트 없음 · 대기 중...");
                return;
            }

            foreach (string port in ports)
            {
                foreach (int baud in _baudCandidates)
                {
                    if (ct.IsCancellationRequested) return;

                    SetState(GpsConnectionState.Searching, $"{port} @ {baud}bps 확인 중...");
                    if (await ProbePortAsync(port, baud, ct).ConfigureAwait(false))
                    {
                        ConnectedPort = port;
                        ConnectedBaud = baud;
                        lock (_sync) _lastDataUtc = DateTime.UtcNow;
                        SetState(GpsConnectionState.Connected, $"{port} @ {baud:N0}bps 연결됨");
                        return;
                    }
                }
            }
        }

        /// <summary>한 포트/baud 조합을 열고 유효 NMEA 수신 여부로 GPS 여부를 판별합니다.</summary>
        private async Task<bool> ProbePortAsync(string port, int baud, CancellationToken ct)
        {
            try
            {
                _sentenceSeen = false;
                _gps.Connect(port, baud);
            }
            catch
            {
                SafeDisconnect();
                return false;
            }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ProbeTimeoutMs)
            {
                if (ct.IsCancellationRequested) { SafeDisconnect(); return false; }
                if (_sentenceSeen) return true; // 연결 유지
                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            SafeDisconnect();
            return false;
        }

        private void SafeDisconnect()
        {
            try { _gps.Disconnect(); } catch { }
            ConnectedPort = null;
            ConnectedBaud = 0;
        }

        private void SetState(GpsConnectionState state, string detail)
        {
            State = state;
            // 상태가 실제로 바뀌었을 때만 이벤트 발생 (Searching 세부 메시지는 매번 갱신)
            if (_reportedState != state || state == GpsConnectionState.Searching)
            {
                _reportedState = state;
                StateChanged?.Invoke(state, detail);
            }
        }

        // ─── IDisposable ──────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _gps.SentenceReceived -= OnSentence;
            _gps.ErrorOccurred    -= OnError;
            _gps.Dispose();
            _cts?.Dispose();
        }
    }
}
