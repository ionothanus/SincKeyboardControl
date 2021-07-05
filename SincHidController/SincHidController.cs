using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Device.Net;
using Hid.Net;
using Hid.Net.Windows;

namespace SincKeyboardControl.SincHid
{
    internal static class JMStrings
    {
        public static readonly string REQUEST_SELECT_WINDOWS = "\x00\x02JMLS0";
        public static readonly string REQUEST_SELECT_MAC = "\x00\x02JMLS1";
        public static readonly string REQUEST_LAYER_STATUS = "\x00\x02JMLR";
        public static readonly string RESPONSE_MAC = "\x00\x02JML\x0e";
        public static readonly string RESPONSE_WINDOWS = "\x00\x02JML\x0f";
        public static readonly string EVENT_LAYER_WINDOWS = "JML0";
        public static readonly string EVENT_LAYER_MAC = "JML1";
        public static readonly int SHORTEST_STRING = 4;
    }

    // TODO: 
    // - wrap all comms in polling task, splitting write & read, so we can monitor for device events?
    // - once done, implement event for layer change

    public class SincHidController : IDisposable, INotifyPropertyChanged
    {
        private const int VID = 0xCB10;
        private const int PID = 0x1267;
        private const ushort USAGE_PAGE = 0xFF60;
        private const int USAGE = 0x61;

        private HidDevice device;

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

        public void CloseDevice()
        {
            device.Close();
            DriverConnected = false;
        }

        private async Task<TransferResult> SendRequestAndRetrieveResponseAsync(string request)
        {
            byte[] data = Encoding.ASCII.GetBytes(request);
            byte[] realData = new byte[65];
            Array.Copy(data, realData, data.Length);

            return await device.WriteAndReadAsync(realData).ConfigureAwait(false);
        }

        public async Task<bool> UpdateLayerStatus()
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

        public async Task<bool> RequestLayer(SincLayerState layer)
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
