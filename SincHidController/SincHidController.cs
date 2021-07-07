using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Device.Net;
using Hid.Net;
using Hid.Net.Windows;

namespace SincKeyboardControl.SincHid
{
    internal static class JMStrings
    {
        public static readonly string REQUEST_SELECT_WINDOWS = "\x00\x02JMLS0"; // transmit to kb: request layer 0 (Windows)
        public static readonly string REQUEST_SELECT_MAC = "\x00\x02JMLS1"; // xmit to kb: request layer 1 (Mac)
        public static readonly string REQUEST_LAYER_STATUS = "\x00\x02JMLR"; // xmit to kb: report current layer
        public static readonly string RESPONSE_WINDOWS = "\x00\x02JML\x0f"; // recv from kb: response to layer request - Windows selected
        public static readonly string RESPONSE_MAC = "\x00\x02JML\x0e"; // recv from kb: response to layer request - Mac selected
        public static readonly string EVENT_LAYER_WINDOWS = "\x00\x02JML0"; // recv from kb: user changed layer using keyboard - Windows
        public static readonly string EVENT_LAYER_MAC = "\x00\x02JML1"; // recv from kb: user changed layer using keyboard - Mac
        public static readonly int SHORTEST_STRING = 4;
    }

    // TODO:
    // - separate events, rather than abusing PropertyChanged?
    // - monitor connection state?

    public class SincHidController : IDisposable, INotifyPropertyChanged
    {
        private const int VID = 0xCB10;
        private const int PID = 0x1267;
        private const ushort USAGE_PAGE = 0xFF60;
        private const int USAGE = 0x61;
        private const int POLLING_INTERVAL = 500;

        private HidDevice device;

        private bool polling;

        private SincLayerState lastState;
        public SincLayerState LastState
        {
            get => lastState;
            private set
            {
                if (lastState != value)
                {
                    lastState = value;
                    OnPropertyChanged(nameof(LastState));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string info) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));

        private bool driverConnected;
        public bool DriverConnected
        {
            get => driverConnected;
            private set
            {
                if (driverConnected != value)
                {
                    driverConnected = value;
                    OnPropertyChanged(nameof(DriverConnected));
                }
            }
        }
        private bool disposed;

        public SincHidController() => LastState = SincLayerState.Unknown;

        public async Task<bool> OpenDevice()
        {
            if (!DriverConnected)
            {
                var factory = new FilterDeviceDefinition(vendorId: VID, productId: PID, usagePage: USAGE_PAGE).CreateWindowsHidDeviceFactory();
                var deviceDefinitions = (await factory.GetConnectedDeviceDefinitionsAsync().ConfigureAwait(false)).FirstOrDefault((device) => { return device.Usage == USAGE; });
                device = (HidDevice)await factory.GetDeviceAsync(deviceDefinitions).ConfigureAwait(false);

                await device.InitializeAsync().ConfigureAwait(false);

                if (!(device is null))
                {
                    DriverConnected = true;

                    return true;
                }

                return false;
            }

            return false;
        }

        public Task StartPolling(CancellationToken ct)
        {
            if (!polling)
            {
                polling = true;

                return Task.Run(async () =>
                {
                    ct.ThrowIfCancellationRequested();

                    while (!ct.IsCancellationRequested && !disposed)
                    {
                        if (driverConnected)
                        {
                            var result = await device.ReadAsync(ct);

                            if (result.BytesTransferred > 0)
                            {
                                LastState = ParseLayerState(result.Data);
                            }
                        }

                        await Task.Delay(POLLING_INTERVAL);
                    }

                    polling = false;
                });
            }

            return null;
        }

        public void CloseDevice()
        {
            device.Close();
            DriverConnected = false;
        }

        // Transmits data and immediately waits for a response. Used in "one-shot" functions.
        private async Task<TransferResult> SendRequestAndRetrieveResponseAsync(string request)
        {
            byte[] data = Encoding.ASCII.GetBytes(request);
            byte[] realData = new byte[65];
            Array.Copy(data, realData, data.Length);

            return await device.WriteAndReadAsync(realData).ConfigureAwait(false);
        }

        // Merely transmits data - used in "polling" mode as the polling Task will read the response
        private async Task<uint> SendRequestAsync(string request)
        {
            byte[] data = Encoding.ASCII.GetBytes(request);
            byte[] realData = new byte[65];
            Array.Copy(data, realData, data.Length);

            return await device.WriteAsync(realData).ConfigureAwait(false);
        }

        /// <summary>
        /// If connected, sends a request for the layer status. Waits on the polling task to capture
        /// the response & update the state.
        /// </summary>
        /// <returns>(through Task) true if successful, false if error occurred</returns>
        public async Task<bool> UpdateLayerStatusPolling()
        {
            if (driverConnected)
            {
                uint result = await SendRequestAsync(JMStrings.REQUEST_LAYER_STATUS);

                return result > 0;
            }

            return false;
        }

        /// <summary>
        /// If connected, sends a request for the layer status. Waits on the polling task to capture
        /// the response & update the state.
        /// </summary>
        /// <returns>(through Task) true if successful, false if error occurred</returns>
        /// <exception cref="NotSupportedException">If polling mode is active, as the read will conflict with the polling reads. Use <seealso cref="UpdateLayerStatusPolling"/> if polling is enabled.</exception>
        public async Task<bool> UpdateLayerStatusOneshot()
        {
            if (polling)
            {
                throw new NotSupportedException("Cannot use one-shot methods when polling is active");
            }

            if (driverConnected)
            {
                var result = await SendRequestAndRetrieveResponseAsync(JMStrings.REQUEST_LAYER_STATUS);

                if (result.BytesTransferred > 0)
                {
                    LastState = ParseLayerState(result.Data);
                    return true;
                }

                return false;
            }

            return false;
        }

        public async Task<bool> RequestLayerPolling(SincLayerState layer)
        {
            if (driverConnected)
            {
                string request;

                switch (layer)
                {
                    case SincLayerState.Mac:
                        request = JMStrings.REQUEST_SELECT_MAC;
                        break;
                    case SincLayerState.Windows:
                        request = JMStrings.REQUEST_SELECT_WINDOWS;
                        break;
                    default:
                        throw new NotImplementedException($"This controller doesn't know how to request the state {layer}");

                }

                uint result = await SendRequestAsync(request);

                return result > 0;
            }

            return false;
        }

        public async Task<bool> RequestLayerOneshot(SincLayerState layer)
        {
            if (polling)
            {
                throw new NotSupportedException("Cannot use one-shot methods when polling is active");
            }

            if (driverConnected)
            {
                string request;

                switch (layer)
                {
                    case SincLayerState.Mac:
                        request = JMStrings.REQUEST_SELECT_MAC;
                        break;
                    case SincLayerState.Windows:
                        request = JMStrings.REQUEST_SELECT_WINDOWS;
                        break;
                    default:
                        throw new NotImplementedException($"This controller doesn't know how to request the state {layer}");

                }

                var result = await SendRequestAndRetrieveResponseAsync(request);

                if (result.BytesTransferred > 0)
                {
                    LastState = ParseLayerState(result.Data);
                    return true;
                }

                return false;
            }

            return false;
        }

        private SincLayerState ParseLayerState(byte[] data)
        {
            string response = Encoding.ASCII.GetString(data);
            response = response.TrimEnd('\x0');

            if (response == JMStrings.RESPONSE_MAC ||
                response == JMStrings.REQUEST_SELECT_MAC ||
                response == JMStrings.EVENT_LAYER_MAC)
            {
                 return SincLayerState.Mac;
            }
            else if (response == JMStrings.RESPONSE_WINDOWS ||
                     response == JMStrings.REQUEST_SELECT_WINDOWS ||
                     response == JMStrings.EVENT_LAYER_WINDOWS)
            {
                return SincLayerState.Windows;
            }
            else
            {
                return SincLayerState.Unknown;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    CloseDevice();
                }

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
