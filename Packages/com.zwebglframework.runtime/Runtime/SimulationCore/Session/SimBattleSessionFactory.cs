namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimBattleSessionFactory : ISimBattleSessionFactory
    {
        public ISimBattleSession Create(byte[] canonicalManifestBytes, ISimLogger logger = null)
        {
            var manifest = DeterministicContentManifest.Parse(canonicalManifestBytes);
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 256, logger);
            return new SimBattleSession(world, manifest, logger);
        }
    }
}
