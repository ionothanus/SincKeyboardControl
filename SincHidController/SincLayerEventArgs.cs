using System;

namespace SincKeyboardControl.SincHid
{
    public class SincLayerEventArgs : EventArgs
    {
        public SincLayerState State { get; private set; }

        public SincLayerEventArgs(SincLayerState state) => State = state;
    }
}
