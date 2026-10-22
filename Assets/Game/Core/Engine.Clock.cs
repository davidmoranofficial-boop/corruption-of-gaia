using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loran.Core
{
    /// <summary>
    /// The clock out in the world (David, 21 Oct): days should pass slowly, and time runs whether you move or not.
    /// </summary>
    public partial class Engine
    {
        /// <summary>
        /// The world's clock: called every frame with real seconds while you're free in the world, walking or standing.
        /// Returns true when the clock moved on a minute (so the HUD wants redrawing). If time took you down
        /// (starving, or out on your feet) you're moved to the collapse or death scene.
        /// </summary>
        public bool Tick(float realSeconds)
        {
            if (realSeconds <= 0 || !FreeToAct || !Travelling || !string.IsNullOrEmpty(S.result)) return false;
            S.secondsCarry += Math.Min(realSeconds, 1f) * Rules.ClockRate;
            int mins = (int)(S.secondsCarry / 60);
            if (mins <= 0) return false;
            S.secondsCarry -= mins * 60;
            Rules.Advance(S, data, mins, false);
            if (S.hp <= 0 || S.energy <= 0) GoTo(S.scene);
            return true;
        }
    }
}
