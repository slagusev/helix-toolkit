﻿/*
The MIT License (MIT)
Copyright (c) 2018 Helix Toolkit contributors
*/
using SharpDX;
using SharpDX.Direct3D11;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Format = global::SharpDX.DXGI.Format;
#if !NETFX_CORE
namespace HelixToolkit.Wpf.SharpDX.Core
#else
namespace HelixToolkit.UWP.Core
#endif
{
    using Model;
    using Model.Scene;
    using Render;
    using Shaders;
    public interface IPostEffectMeshXRayGrid : IPostEffect
    {
        Color4 Color { set; get; }
        int GridDensity { set; get; }
        float DimmingFactor { set; get; }
        float BlendingFactor { set; get; }
    }
    /// <summary>
    /// 
    /// </summary>
    public class PostEffectMeshXRayGridCore : RenderCoreBase<BorderEffectStruct>, IPostEffectMeshXRayGrid
    {
        #region Variables
        private readonly List<KeyValuePair<SceneNode, IEffectAttributes>> currentCores = new List<KeyValuePair<SceneNode, IEffectAttributes>>();
        private DepthPrepassCore depthPrepassCore;
        #endregion
        #region Properties
        /// <summary>
        /// Gets or sets the name of the effect.
        /// </summary>
        /// <value>
        /// The name of the effect.
        /// </value>
        public string EffectName
        {
            set; get;
        } = DefaultRenderTechniqueNames.PostEffectMeshXRayGrid;

        private Color4 color = global::SharpDX.Color.DarkBlue;
        /// <summary>
        /// Gets or sets the color of the border.
        /// </summary>
        /// <value>
        /// The color of the border.
        /// </value>
        public Color4 Color
        {
            set
            {
                SetAffectsRender(ref color, value);
            }
            get { return color; }
        }

        private int gridDensity = 8;
        /// <summary>
        /// Gets or sets the grid density.
        /// </summary>
        /// <value>
        /// The grid density.
        /// </value>
        public int GridDensity
        {
            set
            {
                SetAffectsRender(ref gridDensity, value);
            }
            get { return gridDensity; }
        }

        private float dimmingFactor = 0.8f;
        /// <summary>
        /// Gets or sets the dim factor on original color
        /// </summary>
        /// <value>
        /// The dim factor.
        /// </value>
        public float DimmingFactor
        {
            set
            {
                SetAffectsRender(ref dimmingFactor, value);
            }
            get { return dimmingFactor; }
        }

        private float blendingFactor = 1f;
        /// <summary>
        /// Gets or sets the blending factor for grid and original mesh color blending
        /// </summary>
        /// <value>
        /// The blending factor.
        /// </value>
        public float BlendingFactor
        {
            set
            {
                SetAffectsRender(ref blendingFactor, value);
            }
            get { return blendingFactor; }
        }
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="PostEffectMeshXRayGridCore"/> class.
        /// </summary>
        public PostEffectMeshXRayGridCore() : base(RenderType.PostProc)
        {
            Color = global::SharpDX.Color.Blue;
        }

        /// <summary>
        /// Gets the model constant buffer description.
        /// </summary>
        /// <returns></returns>
        protected override ConstantBufferDescription GetModelConstantBufferDescription()
        {
            return new ConstantBufferDescription(DefaultBufferNames.BorderEffectCB, BorderEffectStruct.SizeInBytes);
        }

        protected override bool OnAttach(IRenderTechnique technique)
        {
            depthPrepassCore = Collect(new DepthPrepassCore());
            depthPrepassCore.Attach(technique);
            return base.OnAttach(technique);
        }

        protected override void OnDetach()
        {
            depthPrepassCore.Detach();
            depthPrepassCore = null;
            base.OnDetach();
        }
        /// <summary>
        /// Called when [render].
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="deviceContext">The device context.</param>
        protected override void OnRender(RenderContext context, DeviceContextProxy deviceContext)
        {
            var buffer = context.RenderHost.RenderBuffer;
            bool hasMSAA = buffer.ColorBufferSampleDesc.Count > 1;
            var depthStencilBuffer = hasMSAA ? buffer.FullResDepthStencilPool.Get(Format.D32_Float_S8X24_UInt) : buffer.DepthStencilBuffer;
            BindTarget(depthStencilBuffer, buffer.FullResPPBuffer.CurrentRTV, deviceContext, buffer.TargetWidth, buffer.TargetHeight, false);
            if (hasMSAA)
            {
                //Needs to do a depth pass for existing meshes.Because the msaa depth buffer is not resolvable.
                deviceContext.ClearDepthStencilView(depthStencilBuffer, DepthStencilClearFlags.Depth, 1, 0);
                depthPrepassCore.Render(context, deviceContext);
            }
            var frustum = context.BoundingFrustum;
            context.IsCustomPass = true;
            //First pass, draw onto stencil buffer
            for (int i = 0; i < context.RenderHost.PerFrameNodesWithPostEffect.Count; ++i)
            {
                var mesh = context.RenderHost.PerFrameNodesWithPostEffect[i];
                if (context.EnableBoundingFrustum && !mesh.TestViewFrustum(ref frustum))
                {
                    continue;
                }
                if (mesh.TryGetPostEffect(EffectName, out IEffectAttributes effect))
                {
                    currentCores.Add(new KeyValuePair<SceneNode, IEffectAttributes>(mesh, effect));
                    context.CustomPassName = DefaultPassNames.EffectMeshXRayGridP1;
                    var pass = mesh.EffectTechnique[DefaultPassNames.EffectMeshXRayGridP1];
                    if (pass.IsNULL) { continue; }
                    pass.BindShader(deviceContext);
                    pass.BindStates(deviceContext, StateType.BlendState | StateType.DepthStencilState);
                    mesh.Render(context, deviceContext);
                }
            }
            //Second pass, remove not covered part from stencil buffer
            for (int i = 0; i < currentCores.Count; ++i)
            {
                var mesh = currentCores[i].Key;
                context.CustomPassName = DefaultPassNames.EffectMeshXRayGridP2;
                var pass = mesh.EffectTechnique[DefaultPassNames.EffectMeshXRayGridP2];
                if (pass.IsNULL) { continue; }
                pass.BindShader(deviceContext);
                pass.BindStates(deviceContext, StateType.BlendState | StateType.DepthStencilState);
                mesh.Render(context, deviceContext);
            }

            deviceContext.ClearDepthStencilView(depthStencilBuffer, DepthStencilClearFlags.Depth, 1, 0);
            //Thrid pass, draw mesh with grid overlay
            for (int i = 0; i < currentCores.Count; ++i)
            {
                var mesh = currentCores[i].Key;
                var color = Color;
                if (currentCores[i].Value.TryGetAttribute(EffectAttributeNames.ColorAttributeName, out object attribute) && attribute is string colorStr)
                {
                    color = colorStr.ToColor4();
                }
                if (modelStruct.Color != color)
                {
                    modelStruct.Color = color;
                    OnUploadPerModelConstantBuffers(deviceContext);
                }
                context.CustomPassName = DefaultPassNames.EffectMeshXRayGridP3;
                var pass = mesh.EffectTechnique[DefaultPassNames.EffectMeshXRayGridP3];
                if (pass.IsNULL) { continue; }
                pass.BindShader(deviceContext);
                pass.BindStates(deviceContext, StateType.BlendState | StateType.DepthStencilState);
                mesh.Render(context, deviceContext);
            }
            if (hasMSAA)
            {
                deviceContext.ClearRenderTagetBindings();
                buffer.FullResDepthStencilPool.Put(Format.D32_Float_S8X24_UInt, depthStencilBuffer);
            }
            context.IsCustomPass = false;
            currentCores.Clear();
        }

        protected override void OnUpdatePerModelStruct(ref BorderEffectStruct model, RenderContext context)
        {
            modelStruct.Color = color;
            modelStruct.Param.M11 = gridDensity;
            modelStruct.Param.M12 = dimmingFactor;
            modelStruct.Param.M13 = blendingFactor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BindTarget(DepthStencilView dsv, RenderTargetView targetView, DeviceContextProxy context, int width, int height, bool clear = true)
        {
            if (clear)
            {
                context.ClearRenderTargetView(targetView, global::SharpDX.Color.Transparent);
            }
            context.SetRenderTargets(dsv, targetView == null ? null : new RenderTargetView[] { targetView });
            context.SetViewport(0, 0, width, height);
            context.SetScissorRectangle(0, 0, width, height);
        }
    }
}
