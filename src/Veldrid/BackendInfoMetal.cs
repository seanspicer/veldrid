#if !EXCLUDE_METAL_BACKEND
using System.Collections.ObjectModel;
using System.Linq;
using Veldrid.MetalBindings;
using Veldrid.MTL;

namespace Veldrid
{
    /// <summary>
    ///     Exposes Metal-specific functionality,
    ///     useful for interoperating with native components which interface directly with Metal.
    ///     Can only be used on <see cref="GraphicsBackend.Metal" />.
    /// </summary>
    public class BackendInfoMetal
    {
        public ReadOnlyCollection<MTLFeatureSet> FeatureSet { get; }

        public MTLFeatureSet MaxFeatureSet => _gd.MetalFeatures.MaxFeatureSet;
        private readonly MTLGraphicsDevice _gd;

        internal BackendInfoMetal(MTLGraphicsDevice gd)
        {
            _gd = gd;
            FeatureSet = new ReadOnlyCollection<MTLFeatureSet>(_gd.MetalFeatures.ToArray());
        }
    }
}
#endif
