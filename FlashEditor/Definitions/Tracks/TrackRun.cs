namespace FlashEditor.Definitions.Tracks {
    /// <summary>
    ///     One contiguous run of packed bytes inside a track, named for the MIDI field it feeds.
    /// </summary>
    /// <remarks>
    ///     The declaration order <b>is</b> the on-disk order. A packed track lays its runs back to
    ///     back with nothing between them and no length field anywhere, so the only thing that says
    ///     where one ends and the next begins is this sequence together with the event counts the
    ///     opcode stream implies. Reordering these members reorders the file.
    ///     <para>
    ///     Taken from the cursor sequence the client walks at <c>Node_Sub7.java:124-165</c>, which
    ///     assigns one cursor per run in this exact order before it re-interleaves anything. Two of
    ///     the names are wider than they read: <see cref="Note"/> is shared by note-on, note-off and
    ///     polyphonic key pressure (<c>:139</c>), and <see cref="Program"/> is shared by program
    ///     changes and by the two bank-select controllers (<c>:153</c>, <c>:239</c>).
    ///     </para>
    /// </remarks>
    public enum TrackRun {
        /// <summary>Sustain, portamento, all-sound-off, reset and all-notes-off values.</summary>
        SwitchedController = 0,

        /// <summary>Polyphonic key-pressure values.</summary>
        KeyPressure,

        /// <summary>Channel-pressure values.</summary>
        ChannelPressure,

        /// <summary>Pitch-wheel high halves, applied as <c>&lt;&lt; 7</c>.</summary>
        PitchWheelHigh,

        /// <summary>Controller 1 values.</summary>
        Modulation,

        /// <summary>Controller 7 values.</summary>
        Volume,

        /// <summary>Controller 10 values.</summary>
        Pan,

        /// <summary>Note numbers, shared by note-on, note-off and key pressure.</summary>
        Note,

        /// <summary>Note-on velocities.</summary>
        NoteOnVelocity,

        /// <summary>Values for every controller with no run of its own.</summary>
        OtherController,

        /// <summary>Note-off velocities.</summary>
        NoteOffVelocity,

        /// <summary>Controller 33 values.</summary>
        ModulationLsb,

        /// <summary>Controller 39 values.</summary>
        VolumeLsb,

        /// <summary>Controller 42 values.</summary>
        PanLsb,

        /// <summary>Program numbers, shared with the bank-select controllers 0 and 32.</summary>
        Program,

        /// <summary>Pitch-wheel low halves, which must be read signed - bit 7 carries upward.</summary>
        PitchWheelLow,

        /// <summary>Controller 99 values.</summary>
        NrpnMsb,

        /// <summary>Controller 98 values.</summary>
        NrpnLsb,

        /// <summary>Controller 101 values.</summary>
        RpnMsb,

        /// <summary>Controller 100 values.</summary>
        RpnLsb,

        /// <summary>Tempo triplets, three bytes per set-tempo event.</summary>
        Tempo
    }
}
