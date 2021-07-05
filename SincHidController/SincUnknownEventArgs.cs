using System;

namespace SincKeyboardControl.SincHid
{
    public class SincUnknownEventArgs : EventArgs
    {
        public byte[] Data { get; private set; }
        public int ReportId { get; private set; }

        public bool Error { get; private set; }

        public SincUnknownEventArgs(byte[] data, int reportId, bool error)
        {
            Data = data;
            ReportId = reportId;
            Error = error;
        }
    }
}
