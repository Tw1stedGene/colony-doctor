using System.Collections.Generic;
using System.Text;
using ColonyDoctor.Sampling;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyDoctor.Construction
{
    /// <summary>
    /// Touching blueprints and frames that form one planned build (spec: Construction sites). The object itself is the
    /// site's identity: discovery hands it a new set of pieces each cycle when the matcher says it is the same site.
    /// </summary>
    public sealed class ConstructionSite : TrackedProblem
    {
        private const int MaxTitleGroups = 3;
        private const int MaxSelect = 80;

        private readonly Map map;
        internal readonly List<Thing> Pieces = new List<Thing>();
        internal readonly List<int> CellKeys = new List<int>();
        internal readonly HashSet<int> CellSet = new HashSet<int>();
        internal readonly HashSet<Thing> PieceSet = new HashSet<Thing>();

        private string title, location;
        private int labelsVersion = -1, piecesVersion;

        private static readonly Dictionary<string, int> TitleCounts = new Dictionary<string, int>();
        private static readonly List<KeyValuePair<string, int>> TitleOrder = new List<KeyValuePair<string, int>>();

        public ConstructionSite(Map map, int phase) : base(phase) => this.map = map;

        public override ProblemKind Kind => ProblemKind.Construction;
        public override bool IsGone => Pieces.Count == 0;
        public int PieceCount => Pieces.Count;

        internal void SetPieces(List<Thing> pieces, List<int> cells)
        {
            Pieces.Clear();
            Pieces.AddRange(pieces);
            // Stable order for material allocation (spec: Materials).
            Pieces.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            PieceSet.Clear();
            for (int i = 0; i < Pieces.Count; i++) PieceSet.Add(Pieces[i]);
            CellKeys.Clear();
            CellKeys.AddRange(cells);
            CellSet.Clear();
            for (int i = 0; i < cells.Count; i++) CellSet.Add(cells[i]);
            piecesVersion++;
        }

        public override string Title
        {
            get
            {
                RefreshLabels();
                return title;
            }
        }

        public override string Location
        {
            get
            {
                RefreshLabels();
                return location;
            }
        }

        public override string Extra => "ColonyDoctor_PiecesLeft".Translate(Pieces.Count.Named("COUNT"));

        public override string Explain(in Core.Diagnosis d) => ConstructionExplanations.For(in d);

        public override void JumpTo()
        {
            if (Pieces.Count == 0) return;
            CameraJumper.TryJump(Center(), map, CameraJumper.MovementMode.Pan);
            Find.Selector.ClearSelection();
            for (int i = 0; i < Pieces.Count && i < MaxSelect; i++)
            {
                if (Pieces[i].Spawned) Find.Selector.Select(Pieces[i], false, true);
            }
        }

        private IntVec3 Center()
        {
            int x = 0, z = 0, n = 0;
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (!Pieces[i].Spawned) continue;
                x += Pieces[i].Position.x;
                z += Pieces[i].Position.z;
                n++;
            }
            return n == 0 ? Pieces[0].Position : new IntVec3(x / n, 0, z / n);
        }

        public static BuildableDef BuiltDef(Thing piece) => piece.def.entityDefToBuild ?? piece.def;

        public static ThingDef BuiltStuff(Thing piece) => (piece as IConstructible)?.EntityToBuildStuff();

        private void RefreshLabels()
        {
            if (labelsVersion == piecesVersion && title != null) return;
            labelsVersion = piecesVersion;

            // Title: "Granite wall ×14, wooden door ×1", most numerous first.
            TitleCounts.Clear();
            for (int i = 0; i < Pieces.Count; i++)
            {
                string label = GenLabel.ThingLabel(BuiltDef(Pieces[i]), BuiltStuff(Pieces[i]), 1);
                TitleCounts.TryGetValue(label, out int c);
                TitleCounts[label] = c + 1;
            }
            TitleOrder.Clear();
            TitleOrder.AddRange(TitleCounts);
            TitleOrder.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            var sb = new StringBuilder();
            for (int i = 0; i < TitleOrder.Count && i < MaxTitleGroups; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(i == 0 ? TitleOrder[i].Key.CapitalizeFirst() : TitleOrder[i].Key).Append(" ×").Append(TitleOrder[i].Value);
            }
            if (TitleOrder.Count > MaxTitleGroups) sb.Append(", …");
            title = sb.ToString();
            location = ComputeLocation();
        }

        /// <summary>Room the site is in, or the compass direction to the nearest colony building (spec: Construction sites).</summary>
        private string ComputeLocation()
        {
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (!Pieces[i].Spawned) continue;
                Room room = Pieces[i].Position.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors && room.Role != null && room.Role != RoomRoleDefOf.None
                    && room.Role.defName != "Room") // the generic role says nothing useful
                {
                    return "ColonyDoctor_SiteInRoom".Translate(room.Role.label.Named("ROOM"));
                }
            }

            IntVec3 c = Center();
            Building nearest = null;
            float best = 30f * 30f;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building b = buildings[i];
                if (b.def.building == null || b.def.designationCategory == null) continue;
                if (b.def.designationCategory.defName == "Structure" || b.def.designationCategory.defName == "Floors") continue;
                if (b.def.designationCategory.defName == "Power" && b.def.Size == IntVec2.One) continue; // conduits
                float d = (b.Position - c).LengthHorizontalSquared;
                if (d < best)
                {
                    best = d;
                    nearest = b;
                }
            }
            if (nearest == null) return "ColonyDoctor_SiteOutdoors".Translate();
            return "ColonyDoctor_SiteNear".Translate(Direction(nearest.Position, c).Named("DIR"), nearest.LabelShort.Named("BUILDING"));
        }

        private static string Direction(IntVec3 from, IntVec3 to)
        {
            int dx = to.x - from.x, dz = to.z - from.z;
            if (Mathf.Abs(dx) < 3 && Mathf.Abs(dz) < 3) return "ColonyDoctor_DirNextTo".Translate();
            string key = Mathf.Abs(dx) > Mathf.Abs(dz) * 2 ? (dx > 0 ? "East" : "West")
                : Mathf.Abs(dz) > Mathf.Abs(dx) * 2 ? (dz > 0 ? "North" : "South")
                : (dz > 0 ? "North" : "South") + (dx > 0 ? "east" : "west");
            return ("ColonyDoctor_Dir" + key).Translate();
        }
    }
}
