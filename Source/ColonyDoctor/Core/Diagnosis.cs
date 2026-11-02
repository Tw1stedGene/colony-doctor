namespace ColonyDoctor.Core
{
    /// <summary>
    /// Result of diagnosing one bill at one moment. A value type with reference "subjects" (pawns, things, stockpiles)
    /// and numbers, so sampling never allocates; explanation strings are built only when the UI renders.
    /// </summary>
    public struct Diagnosis
    {
        public Reason Reason;
        public Detail Detail;

        /// <summary>Primary subject, e.g. the closest candidate pawn or the nearest blocked stack.</summary>
        public object SubjectA;
        /// <summary>Secondary subject, e.g. an ingredient def or the stockpile involved.</summary>
        public object SubjectB;
        /// <summary>Third subject, e.g. the building a skill requirement belongs to.</summary>
        public object SubjectC;

        public int ValueA;
        public int ValueB;
        public int ValueC;

        /// <summary>Text owned by the game (e.g. a captured failure reason). Never built during sampling.</summary>
        public string Text;

        public int Tick;

        /// <summary>Construction sites: pieces this reason applies to, and pieces in the site ("12 of 14").</summary>
        public int PieceCount;
        public int PieceTotal;

        public static Diagnosis Of(Reason reason, Detail detail = Detail.None)
            => new Diagnosis { Reason = reason, Detail = detail };
    }
}
