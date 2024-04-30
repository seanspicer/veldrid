namespace Veldrid.OpenGL
{
    internal interface IOpenGLDeferredResource
    {
        bool Created { get; }
        void EnsureResourcesCreated();
        void DestroyGLResources();
    }
}
