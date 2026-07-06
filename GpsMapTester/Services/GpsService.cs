using System;
using System.IO.Ports;
using System.Text;

namespace GpsMapTester.Services
{
    /// <summary>
    /// GlobalSat BU-353N GPS 장치와의 시리얼 통신을 담당하는 서비스
    /// - DataReceived 이벤트를 통해 수신 데이터를 라인 단위로 버퍼링
    /// - NMEA 체크섬 검증 후 유효한 문장만 SentenceReceived 이벤트로 전달
    /// </summary>
    public class GpsService : IDisposable
    {
        private SerialPort _serialPort;
        private string _buffer = string.Empty;
        private bool _disposed = false;

        // ─── 이벤트 ───────────────────────────────────────────────────────
        /// <summary>체크섬이 유효한 NMEA 문장 수신 시 발생 (백그라운드 스레드)</summary>
        public event Action<string> SentenceReceived;

        /// <summary>$ 로 시작하는 모든 라인 수신 시 발생. isValid = 체크섬 통과 여부</summary>
        public event Action<string, bool> RawSentenceReceived;

        /// <summary>시리얼 포트 오류 발생 시 (백그라운드 스레드)</summary>
        public event Action<Exception> ErrorOccurred;

        // ─── 상태 ─────────────────────────────────────────────────────────
        /// <summary>현재 포트가 열려 있는지 여부</summary>
        public bool IsConnected => _serialPort?.IsOpen ?? false;

        /// <summary>현재 연결된 포트 이름 (미연결 시 null)</summary>
        public string PortName => _serialPort?.IsOpen == true ? _serialPort.PortName : null;

        // ─── 연결 / 해제 ──────────────────────────────────────────────────
        /// <summary>
        /// 지정된 COM 포트로 GPS 장치에 연결합니다.
        /// BU-353N 기본 설정: 8N1
        /// </summary>
        public void Connect(string portName, int baudRate)
        {
            if (_serialPort?.IsOpen == true)
                throw new InvalidOperationException("이미 연결되어 있습니다.");

            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Encoding   = Encoding.ASCII,
                ReadTimeout  = 3000,
                WriteTimeout = 500,
                Handshake  = Handshake.None,
                RtsEnable  = false,
                DtrEnable  = false
            };

            _serialPort.DataReceived  += OnDataReceived;
            _serialPort.ErrorReceived += OnErrorReceived;

            _buffer = string.Empty;
            _serialPort.Open();
        }

        /// <summary>GPS 장치 연결을 해제합니다.</summary>
        public void Disconnect()
        {
            if (_serialPort == null) return;

            _serialPort.DataReceived  -= OnDataReceived;
            _serialPort.ErrorReceived -= OnErrorReceived;

            if (_serialPort.IsOpen)
            {
                try { _serialPort.Close(); }
                catch { /* 이미 닫혔거나 오류 무시 */ }
            }

            try { _serialPort.Dispose(); } catch { }
            _serialPort = null;
            _buffer = string.Empty;
        }

        // ─── 수신 처리 ────────────────────────────────────────────────────
        /// <summary>
        /// DataReceived 이벤트 핸들러 (백그라운드 스레드)
        /// 수신 데이터를 버퍼에 누적하고 '\n' 기준으로 라인 단위 분리 처리
        /// </summary>
        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var port = _serialPort;
                if (port == null || !port.IsOpen) return;

                string incoming = port.ReadExisting();
                _buffer += incoming;

                int idx;
                while ((idx = _buffer.IndexOfAny(new[] { '\n', '\r' })) >= 0)
                {
                    string line = _buffer.Substring(0, idx).Trim('\r', '\n');
                    _buffer = _buffer.Substring(idx + 1);

                    if (string.IsNullOrEmpty(line) || !line.StartsWith("$")) continue;

                    bool valid = ValidateChecksum(line);
                    RawSentenceReceived?.Invoke(line, valid);
                    if (valid)
                        SentenceReceived?.Invoke(line);
                }

                // 버퍼 오버플로우 방지 (4KB 초과 시 초기화)
                if (_buffer.Length > 4096)
                    _buffer = string.Empty;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }

        private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            ErrorOccurred?.Invoke(new Exception($"시리얼 포트 하드웨어 오류: {e.EventType}"));
        }

        // ─── NMEA 체크섬 검증 ─────────────────────────────────────────────
        /// <summary>
        /// NMEA 체크섬 검증: '$'와 '*' 사이 모든 문자의 XOR 결과를 비교
        /// 체크섬이 없는 문장은 통과(true)로 처리
        /// </summary>
        private static bool ValidateChecksum(string sentence)
        {
            int starIdx = sentence.LastIndexOf('*');

            // 체크섬 없는 문장은 통과
            if (starIdx < 0) return true;

            // 체크섬 파싱 불가 시 거부
            if (starIdx + 2 >= sentence.Length) return false;
            if (!int.TryParse(sentence.Substring(starIdx + 1, 2),
                System.Globalization.NumberStyles.HexNumber, null, out int expected))
                return false;

            // '$'(인덱스 0) 제외, '*' 직전까지 XOR
            int calculated = 0;
            for (int i = 1; i < starIdx; i++)
                calculated ^= sentence[i];

            return calculated == expected;
        }

        // ─── IDisposable ──────────────────────────────────────────────────
        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
    }
}
