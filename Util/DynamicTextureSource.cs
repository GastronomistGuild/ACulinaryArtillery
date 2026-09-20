using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ACulinaryArtillery
{
    /// <summary>
    /// Used as a rough replacement for adding ARL as a dependency.
    /// </summary>
    public class DynamicTextureSource : ITexPositionSource
    {
        private readonly ICoreClientAPI capi;

        // Used for loading dynamic textures
        private readonly Dictionary<string, TextureAtlasPosition?> texturePositions = [];

        private readonly TextureAtlasPosition defaultTexPos;

        public DynamicTextureSource(ICoreClientAPI capi, Block block, string defaultTextureName)
        {
            this.capi = capi;

            defaultTexPos = capi.BlockTextureAtlas.GetPosition(block, defaultTextureName);
        }

        public TextureAtlasPosition GetOrInsertTexture(string name, CompositeTexture texture)
        {
            int textureSubId = ObjectCacheUtil.GetOrCreate(capi, $"{name}texture-{texture}", () =>
            {
                capi.BlockTextureAtlas.GetOrInsertTexture(
                    texture.Base.CopyWithPathPrefixAndAppendixOnce("textures/", ".png"),
                    out var id,
                    out _,
                    new CreateTextureDelegate(() =>
                    {
                        var bmp = capi.Assets.TryGet(texture.Base.CopyWithPathPrefixAndAppendixOnce("textures/", ".png"))?.ToBitmap(capi);
                        if (bmp != null && texture.Alpha != 255) bmp.MulAlpha(texture.Alpha);
                        return bmp;
                    })
                );
                return id;
            });

            texturePositions[name] = capi.BlockTextureAtlas.Positions[textureSubId];
            return texturePositions[name] ?? defaultTexPos;
        }

        public void AddTexturePosition(string name, TextureAtlasPosition texPos)
        {
            texturePositions[name] = texPos;
        }

        public TextureAtlasPosition this[string textureCode]
        {
            get => texturePositions.GetValueOrDefault(textureCode) ?? defaultTexPos;
        }

        public Size2i AtlasSize => capi.BlockTextureAtlas.Size;
    }
}
