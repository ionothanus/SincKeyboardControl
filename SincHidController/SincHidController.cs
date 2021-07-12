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
        public static readonly string REQUEST_SELECT_WINDOWS = "\x00\x02JMLS0"; // xmit to kb: request layer 0 (Windows)
        public static readonly string REQUEST_SELECT_MAC = "\x00\x02JMLS1"; // xmit to kb: request layer 1 (Mac)
        public static readonly string REQUEST_LAYER_STATUS = "\x00\x02JMLR"; // xmit to kb: report current layer
        public static readonly string REQUEST_DISABLE_KEY = "\x00\x02JMLD"; // xmit to kb: request disable layer-switch macro key
        public static readonly string REQUEST_ENABLE_KEY = "\x00\x02JMLE"; // xmit to kb: request disable layer-switch macro key
        public static readonly string RESPONSE_WINDOWS = "\x00\x02JML\x0f"; // recv from kb: response to layer request - Windows selected
        public static readonly string RESPONSE_MAC = "\x00\x02JML\x0e"; // recv from kb: response to layer request - Mac selected
        public static readonly string RESPONSE_KEY_DISABLED = "\x00\x02JMLDS"; // recv from kb: response to layer request - key disabled
        public static readonly string RESPONSE_KEY_ENABLED = "\x00\x02JMLES"; // recv from kb: response to layer request - key enabled
        public static readonly string EVENT_LAYER_WINDOWS = "\x00\x02JML0"; // recv from kb: user changed layer using keyboard - Windows
        public static readonly string EVENT_LAYER_MAC = "\x00\x02JML1"; // recv from kb: user changed layer using keyboard - Mac
        public static readonly int SHORTEST_STRING = 4;
    }

    // TODO:
    // - separate events, rather than abusing PropertyChanged?

    public class SincHidController : IDisposable, INotifyPropertyChanged
    {
        private const int VID = 0xCB10;
        private const int PID = 0x1267;
        private const ushort USAGE_PAGE = 0xFF60;
        private const int USAGE = 0x61;
        private const int POLLING_INTERVAL = 1000;

        private HidDevice device;
        private IDeviceFactory deviceManager;
        private DeviceListener deviceListener;

        private bool polling;
        private Task pollingTask;
        private CancellationTokenSource pollingTaskCts;

        private SincLayerState? lastState;
        public SincLayerState? LastState
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

        public event EventHandler DeviceConnected;
        public event EventHandler DeviceDisconnected;

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
                    OnPropertyChanged(nameof(DriverDisconnected));
                }
            }
        }
        public bool DriverDisconnected
        {
            get => !driverConnected;
        }

        private bool macroKeyDisabled;
        public bool MacroKeyDisabled
        {
            get => macroKeyDisabled;
            private set
            {
                if (macroKeyDisabled != value)
                {
                    macroKeyDisabled = value;
                   OnPropertyChanged(nameof(MacroKeyDisabled));
                }
            }
        }

        private bool disposed;

        public SincHidController()
        {
            LastState = null;
            deviceManager = new FilterDeviceDefinition(vendorId: VID, productId: PID, usagePage: USAGE_PAGE).CreateWindowsHidDeviceFactory();
            deviceListener = new DeviceListener(deviceManager, POLLING_INTERVAL, null);

            deviceListener.DeviceInitialized += DeviceListener_DeviceInitialized;
            deviceListener.DeviceDisconnected += DeviceListener_DeviceDisconnected;
        }

        private void DeviceListener_DeviceDisconnected(object sender, DeviceEventArgs e)
        {
            CloseDevice();
            device = null;
            DeviceDisconnected?.Invoke(this, new EventArgs());
        }

        private async void DeviceListener_DeviceInitialized(object sender, DeviceEventArgs e)
        {
            await ConnectNewDevice();
            DeviceConnected?.Invoke(this, new EventArgs());
        }

        /// <summary>
        /// Begins device listener to open device.
        /// </summary>
        /// <returns>true</returns>
        public bool OpenDevice()
        {
            deviceListener.Start();
            return true;
        }

        private async Task<bool> ConnectNewDevice()
        {
            if (!DriverConnected)
            {
                var deviceDefinitions = (await deviceManager.GetConnectedDeviceDefinitionsAsync().ConfigureAwait(false)).FirstOrDefault((device) => { return device.Usage == USAGE; });
                device = (HidDevice)await deviceManager.GetDeviceAsync(deviceDefinitions).ConfigureAwait(false);

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

        /// <summary>
        /// Acquires a Task for polling for event messages from the device.
        /// </summary>
        /// <param name="cts">CancellationToken to terminate polling.</param>
        /// <returns>The polling task.</returns>
        public Task CreatePollingTask(CancellationTokenSource cts)
        {
            if (!polling)
            {
                polling = true;

                var ct = cts.Token;
                pollingTaskCts = cts;

                pollingTask = Task.Run(async () =>
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        while (!ct.IsCancellationRequested && !disposed)
                        {
                            if (driverConnected)
                            {
                                var result = await device?.ReadAsync(ct);

                                if (result.BytesTransferred > 0)
                                {
                                    LastState = ParseLayerState(result.Data);
                                }
                            }
                        }

                        polling = false;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                    finally
                    {
                        polling = false;
                    }
                });

                return pollingTask;
            }

            return null;
        }

        /// <summary>
        /// Close the device connection, stopping polling and closing the connection.
        /// </summary>
        public void CloseDevice()
        {
            pollingTaskCts?.Cancel();
            device?.Close();
            DriverConnected = false;
            MacroKeyDisabled = false;
            LastState = null;
            pollingTask?.Dispose();
        }

        /// <summary>
        /// Transmits data and immediately waits for a response. Used in "one-shot" functions.
        /// </summary>
        /// <param name="request">Command to send to device.</param>
        /// <returns>true if communications successful, false if error</returns>
        private async Task<TransferResult> SendRequestAndRetrieveResponseAsync(string request)
        {
            if (polling)
            {
                throw new NotSupportedException("Cannot use one-shot methods when polling is active");
            }

            byte[] data = Encoding.ASCII.GetBytes(request);
            byte[] realData = new byte[65];
            Array.Copy(data, realData, data.Length);

            return await device.WriteAndReadAsync(realData).ConfigureAwait(false);
        }

        /// <summary>
        /// Only transmits data - used in "polling" mode as the polling Task will read the response
        /// </summary>
        /// <param name="request">Command to send to device.</param>
        /// <returns>number of bytes transmitted</returns>
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
        /// If connected, sends a request for the layer status, and immediately awaits the response.
        /// </summary>
        /// <returns>(through Task) true if successful, false if error occurred</returns>
        /// <exception cref="NotSupportedException">If polling mode is active, as the read will conflict with the polling reads. Use <seealso cref="UpdateLayerStatusPolling"/> if polling is enabled.</exception>
        public async Task<bool> UpdateLayerStatusOneshot()
        {
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

        /// <summary>
        /// If connected, sends a request to disable the macro key. Waits on the polling task to capture
        /// the response & update the state.
        /// </summary>
        /// <returns>(through Task) true if successful, false if error occurred</returns>
        public async Task<bool> SetMacroKeyPolling(SincMacroKeyState state)
        {
            if (driverConnected)
            {
                string request = state == SincMacroKeyState.Disabled ? JMStrings.REQUEST_DISABLE_KEY : JMStrings.REQUEST_ENABLE_KEY;
                uint result = await SendRequestAsync(request);

                return result > 0;
            }

            return false;
        }

        /// <summary>
        /// If connected, sends a request to disable the macro key, and immediately awaits the response.
        /// </summary>
        /// <returns>(through Task) true if successful, false if error occurred</returns>
        /// <exception cref="NotSupportedException">If polling mode is active, as the read will conflict with the polling reads. Use <seealso cref="UpdateLayerStatusPolling"/> if polling is enabled.</exception>
        public async Task<bool> SetMacroKeyOneshot(SincMacroKeyState state)
        {
            if (driverConnected)
            {
                string request = state == SincMacroKeyState.Disabled ? JMStrings.REQUEST_DISABLE_KEY : JMStrings.REQUEST_ENABLE_KEY;
                var result = await SendRequestAndRetrieveResponseAsync(request);

                if (result.BytesTransferred > 0)
                {
                    _ = ParseLayerState(result.Data);
                    return true;
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// Requests a change to a specific layer. Leaves the polling task to read the response and update the state.
        /// </summary>
        /// <param name="layer">The name of the layer to change to.</param>
        /// <returns>true if request sent, false if error</returns>
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

        /// <summary>
        /// Requests a change to a specific layer and immediately awaits the response.
        /// </summary>
        /// <param name="layer">The name of the layer to change to.</param>
        /// <returns>true if request sent, false if error</returns>
        public async Task<bool> RequestLayerOneshot(SincLayerState layer)
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

        private SincLayerState? ParseLayerState(byte[] data)
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
            else if (response == JMStrings.RESPONSE_KEY_DISABLED)
            {
                MacroKeyDisabled = true;
                return LastState;
            }
            else if (response == JMStrings.RESPONSE_KEY_ENABLED)
            {
                MacroKeyDisabled = false;
                return LastState;
            }
            else
            {
                return null;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    CloseDevice();
                    deviceListener.DeviceDisconnected -= DeviceListener_DeviceDisconnected;
                    deviceListener.DeviceInitialized -= DeviceListener_DeviceInitialized;
                    deviceListener.Dispose();
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
