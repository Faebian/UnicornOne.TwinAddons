using System;
using System.Collections.Generic;
using UnicornOne.Abstractions.Twin;

namespace TwinAddons.GaNLinearBilayerDesorption
{
    /// <summary>
    /// The normal GaN RHEED recovery is SmoothStep01(1 - coverage/saturation).
    /// Inverting that exact transfer function lets the surface removal produce a
    /// RHEED trace with temperature-governed *central slope*, eased at both ends.
    /// Thus LR also observes the new, shared physical coverage trajectory.
    /// </summary>
    public sealed class GaNLinearBilayerDesorption : ITwinAddon, ITwinGaNBilayerKineticsOverride,
        ITwinGaNTransitionObserver
    {
        private readonly object _sync = new object();
        private bool _active;
        private double _distance;
        private double _initialIntensityFraction;
        public string Id => "GaNLinearBilayerDesorption";
        public string DisplayName => "GaN Linear Bilayer Desorption";
        public string Version => "1.0.0";
        public string Description => "Sets the central RHEED recovery gradient from real substrate temperature, with rounded ends.";
        public IEnumerable<TwinParameterDescriptor> DescribeParameters()
        {
            yield return Number("ReferenceTemperatureC", "Reference substrate temperature", 690, 0, 1300, "C", "REAL temperature, after thermocouple offset.");
            yield return Number("ReferenceSlopeIntensityPerSecond", "Central gradient at reference", 3, 0.01, 100, "a.u./s", "RHEED intensity recovery gradient at reference temperature.");
            yield return Number("SlopeTemperatureCoefficient", "Gradient temperature coefficient", 0.02, -1, 1, "a.u./(s C)", "Change of central intensity slope per degree C.");
            yield return Number("EdgeFraction", "Start/finish easing width", 0.12, 0.01, 0.45, "fraction", "Fraction of total transition distance used for each curved edge.");
        }
        private static TwinParameterDescriptor Number(string key, string name, double initial,
            double min, double max, string units, string description)
        {
            return new TwinParameterDescriptor
            {
                Path = "Addons.GaNLinearBilayerDesorption." + key, Module = "Addons",
                DisplayName = name, ValueType = "double", DefaultValue = initial,
                Minimum = min, Maximum = max, Units = units, Description = description, Live = true
            };
        }
        public void OnSurfaceCreated(ITwinAddonHost host) { Reset(); }
        public void OnEnabledChanged(bool enabled, ITwinAddonHost host) { Reset(); }
        public void OnGaNTransition(GaNSurfaceTransitionEvent transition, ITwinAddonHost host)
        {
            if (transition.Transition == GaNSurfaceTransition.BilayerFormed ||
                transition.Transition == GaNSurfaceTransition.DropletsDesorbed ||
                transition.Transition == GaNSurfaceTransition.BilayerDesorbed)
                Reset();
        }
        private void Reset() { lock (_sync) { _active = false; _distance = 0; } }
        public double CalculateBilayerCoverageDelta(GaNBilayerContext c, ITwinAddonHost host)
        {
            lock (_sync)
            {
                if (c.CoverageAtomsPerNm2 <= 0 || c.DeltaSeconds <= 0 || c.SaturationAtomsPerNm2 <= 0)
                    return c.NativeCoverageDeltaAtomsPerNm2;
                double range = c.CleanRheedIntensity - c.SaturatedRheedIntensity;
                if (range <= 0) return c.NativeCoverageDeltaAtomsPerNm2;

                double r = Clamp01(1.0 - c.CoverageAtomsPerNm2 / c.SaturationAtomsPerNm2);
                double currentFraction = SmoothStep(r);
                if (!_active)
                {
                    _active = true;
                    _distance = 0;
                    _initialIntensityFraction = currentFraction;
                }
                double edge = host.GetDouble("Addons.GaNLinearBilayerDesorption.EdgeFraction");
                double targetSlope = host.GetDouble("Addons.GaNLinearBilayerDesorption.ReferenceSlopeIntensityPerSecond") +
                    host.GetDouble("Addons.GaNLinearBilayerDesorption.SlopeTemperatureCoefficient") *
                    (c.RealSubstrateTemperatureC - host.GetDouble("Addons.GaNLinearBilayerDesorption.ReferenceTemperatureC"));
                // No negative desorption or infinite waiting if temperatures are out of model range.
                targetSlope = Math.Max(0.001, targetSlope);
                double remainingIntensity = Math.Max(1e-8, 1.0 - _initialIntensityFraction);
                // The middle derivative of Ease(distance) is 1/(1-edge).
                // Therefore dI/dt in its middle is exactly targetSlope.
                _distance = Clamp01(_distance + targetSlope * c.DeltaSeconds *
                    (1.0 - edge) / (range * remainingIntensity));
                double progress = Ease(_distance, edge);
                double requiredIntensityFraction = _initialIntensityFraction + remainingIntensity * progress;
                double requiredR = InverseSmoothStep(Clamp01(requiredIntensityFraction));
                double nextCoverage = _distance >= 1 ? 0 :
                    c.SaturationAtomsPerNm2 * (1.0 - requiredR);
                // Never add Ga and never override a current value above the bilayer threshold.
                return Math.Min(0, Math.Max(-c.CoverageAtomsPerNm2, nextCoverage - c.CoverageAtomsPerNm2));
            }
        }
        private static double Ease(double u, double edge)
        {
            if (u <= edge) return u * u / (2 * edge * (1 - edge));
            if (u <= 1 - edge) return (u - edge / 2) / (1 - edge);
            return 1 - (1 - u) * (1 - u) / (2 * edge * (1 - edge));
        }
        private static double SmoothStep(double x) { return x * x * (3 - 2 * x); }
        private static double Clamp01(double x) { return Math.Max(0, Math.Min(1, x)); }
        private static double InverseSmoothStep(double target)
        {
            double lo = 0, hi = 1;
            for (int i = 0; i < 30; ++i)
            {
                double mid = (lo + hi) / 2;
                if (SmoothStep(mid) < target) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }
    }
}
