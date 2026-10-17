using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ACulinaryArtillery
{
    public class MixingBowlTopRenderer : IRenderer
    {
        internal bool ShouldRender;
        internal bool ShouldRotateManual;
        internal bool ShouldRotateAutomated;

        public BEBehaviorMPConsumer? mechPowerPart;

        private ICoreClientAPI api;
        private BlockPos pos;

        MeshRef meshref;
        MeshRef meshrefWithPot;
        public Matrixf ModelMat = new Matrixf();

        public bool HasPot;

        public float AngleRad;

        public MixingBowlTopRenderer(ICoreClientAPI capi, BlockPos pos, MeshData mesh, MeshData meshWithPot)
        {
            this.api = capi;
            this.pos = pos;
            meshref = capi.Render.UploadMesh(mesh);
            meshrefWithPot = capi.Render.UploadMesh(meshWithPot);
        }

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (meshref == null || !ShouldRender) return;

            IRenderAPI rpi = api.Render;
            Vec3d camPos = api.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.Tex2D = api.BlockTextureAtlas.AtlasTextures[0].TextureId;

            float translateY = HasPot ? 12.5f / 16f : 11 / 16f;

            prog.ModelMatrix = ModelMat
                .Identity()
                .Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z)
                .Translate(0.5f, translateY, 0.5f)
                .RotateY(AngleRad)
                .Translate(-0.5f, 0, -0.5f)
                .Values
            ;

            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            rpi.RenderMesh(HasPot ? meshrefWithPot : meshref);
            prog.Stop();

            if (ShouldRotateManual)
            {
                AngleRad += deltaTime * 40 * GameMath.DEG2RAD;
            }

            if (ShouldRotateAutomated)
            {
                AngleRad = mechPowerPart?.AngleRad ?? AngleRad;
            }
        }

        public void Dispose()
        {
            api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);

            meshref?.Dispose();
        }
    }
}
