﻿namespace gsudo
{
    class ElevationRequest
    {
        public string FileName { get; set; }
        public string Arguments { get; set; }
        public string StartFolder { get; set; }
        public bool NewWindow { get; set; }
        public bool ForceWait { get; set; }
    }
}
