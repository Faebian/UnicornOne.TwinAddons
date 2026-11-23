using System;
using System.Collections.Generic;
using UnicornOne.Abstractions.Twin;

namespace TwinAddons.GaNCleanliness
{
    public sealed class GaNCleanliness : ITwinAddon, ITwinGaNTransitionObserver,
        ITwinGaNDropletRateModifier, ITwinAddonLiveState
    {
        private readonly object _sync = new object();
        private double _cleanliness = 0.1;
        private double _initialDropletExcess;
        private double _latchedCleanliness;
        private bool _desorbing;
        public string Id => "GaNCleanliness";
        public string DisplayName => "GaN Cleanliness";
        public string Version => "1.0.0";
        public string Description => "Cleanliness improves by 0.025 per surface stage; slows droplet evaporation when dirty.";
        public IEnumerable<TwinParameterDescriptor> DescribeParameters()
        {
            yield return Number("IncrementPerStage", "Cleanliness increase per stage", 0.025, 0, 0.25, "", "Four stages constitute one complete cycle.");
            yield return Number("MaximumDropletDelaySeconds", "Maximum extra droplet duration", 10.0, 0, 120, "s", "Time penalty at zero cleanliness; 9 seconds at cleanliness 0.1.");
            yield return new TwinParameterDescriptor
            {
                Path = "Addons.GaNCleanliness.Cleanliness", Module = "Addons",
                DisplayName = "Current cleanliness (read only)", Description = "Live per-surface state; resets to 0.1 with a new GaN surface.",
                ValueType = "double", DefaultValue = 0.1, Minimum = 0, Maximum = 1,
                Units = "fraction", ReadOnly = true, Live = true
            };
        }
        private static TwinParameterDescriptor Number(string key, string name, double initial,
            double min, double max, string unit, string description)
        {
            return new TwinParameterDescriptor
            {
                Path = "Addons.GaNCleanliness." + key, Module = "Addons", DisplayName = name,
                DefaultValue = initial, Minimum = min, Maximum = max, ValueType = "double",
                Units = unit, Description = description, Live = true
            };
        }
        public void OnSurfaceCreated(ITwinAddonHost host)
        {
            lock (_sync)
            {
                _cleanliness = 0.1;
                _desorbing = false;
                _initialDropletExcess = 0;
            }
            host.StateChanged("Addons.GaNCleanliness.Cleanliness");
        }
        public void OnEnabledChanged(bool enabled, ITwinAddonHost host)
        {
            lock (_sync) { _desorbing = false; _initialDropletExcess = 0; }
        }
        public void OnGaNTransition(GaNSurfaceTransitionEvent transition, ITwinAddonHost host)
        {
            lock (_sync)
            {
                _cleanliness = Math.Min(1, Math.Round(_cleanliness +
                    host.GetDouble("Addons.GaNCleanliness.IncrementPerStage"), 6));
                if (transition.Transition == GaNSurfaceTransition.DropletsFormed ||
                    transition.Transition == GaNSurfaceTransition.DropletsDesorbed ||
                    transition.Transition == GaNSurfaceTransition.BilayerDesorbed)
                {
                    _desorbing = false;
                    _initialDropletExcess = 0;
                }
            }
            host.StateChanged("Addons.GaNCleanliness.Cleanliness");
        }
        public double ModifyDropletDesorptionFlux(GaNDesorptionContext c, ITwinAddonHost host)
        {
            lock (_sync)
            {
                double native = c.NativeFluxAtomsPerNm2PerSecond;
                if (native <= 0) return c.ProposedFluxAtomsPerNm2PerSecond;
                if (!_desorbing)
                {
                    _desorbing = true;
                    _initialDropletExcess = Math.Max(0, c.CoverageAtomsPerNm2 - c.SaturationAtomsPerNm2);
                    _latchedCleanliness = _cleanliness;
                }
                // For constant temperature, remaining excess reaches zero after native duration + penalty.
                // Recompute rate for changing temperature, but keep the inventory and cleanliness latched.
                double nativeSeconds = _initialDropletExcess / native;
                double extraSeconds = host.GetDouble("Addons.GaNCleanliness.MaximumDropletDelaySeconds") *
                    (1.0 - _latchedCleanliness);
                if (nativeSeconds <= 0) return c.ProposedFluxAtomsPerNm2PerSecond;
                double ratio = nativeSeconds / (nativeSeconds + extraSeconds);
                return c.ProposedFluxAtomsPerNm2PerSecond * ratio;
            }
        }
        public bool TryGetState(string path, out object value)
        {
            lock (_sync)
            {
                if (string.Equals(path, "Addons.GaNCleanliness.Cleanliness", StringComparison.OrdinalIgnoreCase))
                { value = _cleanliness; return true; }
            }
            value = null;
            return false;
        }
    }
}
