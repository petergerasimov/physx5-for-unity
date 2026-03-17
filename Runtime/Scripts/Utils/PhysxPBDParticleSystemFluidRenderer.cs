using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysX5ForUnity
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(36)]
    [AddComponentMenu("PhysX 5/Renderers/PBD Particle System Fluid Renderer")]
    public class PhysxPBDParticleSystemFluidRenderer : MonoBehaviour
    {
        public int NumParticles
        {
            get { return m_numParticles; }
            set { m_numParticles = value; }
        }

        public Vector4[] SharedPositionInvMass
        {
            get { return m_sharedPositionInvMass; }
            set { m_sharedPositionInvMass = value; }
        }

        public Color[] SharedFluidColors
        {
            get { return m_fluidColors; }
            set { m_fluidColors = value; }
        }

        public Material FluidMaterial
        {
            get { return m_fluidMaterial; }
        }

        public int[] ActiveFluidIndices
        {
            get { return m_activeFluidIndices; }
            set { m_activeFluidIndices = value; }
        }

        public PhysxPBDParticleSystem PBDParticleSystem
        {
            get { return m_pbdParticleSystem; }
            set { m_pbdParticleSystem = value; }
        }

        public enum DepthFilterType
        {
            Bilateral = 0,
            NarrowRange = 1
        }

        public virtual void AddActor(PhysxFluidActor actor)
        {
            m_actors.Add(actor);
            m_maxNumActiveIndices += actor.NumParticles;
        }

        public virtual void UpdateColorsBuffer()
        {
            m_colorsBuffer.SetData(m_fluidColors.Take(m_numParticles).ToArray());
        }

        protected virtual void CreateRenderResources()
        {
            CreateComputeBuffer(ref m_particleBuffer, 16, m_numParticles);
            CreateComputeBuffer(ref m_indexBuffer, sizeof(int), m_maxNumActiveIndices);
            m_activeFluidIndices = new int[m_maxNumActiveIndices];

            m_mesh = new Mesh
            {
                name = "PBD Fluid",
                vertices = new Vector3[1]
            };
            m_mesh.SetIndices(new int[m_numParticles], MeshTopology.Points, 0);

            m_prepareFluidMaterial = new Material(m_fluidShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            meshFilter.mesh = m_mesh;

            m_meshRender = GetComponent<MeshRenderer>();
            m_meshRender.material = m_fluidMaterial;
            m_meshRender.receiveShadows = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetSelectedRenderState(m_meshRender, UnityEditor.EditorSelectedRenderState.Hidden);
#endif

            m_anisotropy1Buffer = new ComputeBuffer(m_numParticles, sizeof(float) * 4);
            m_anisotropy2Buffer = new ComputeBuffer(m_numParticles, sizeof(float) * 4);
            m_anisotropy3Buffer = new ComputeBuffer(m_numParticles, sizeof(float) * 4);

            m_colorsBuffer = new ComputeBuffer(m_numParticles, sizeof(float) * 4);
            m_colorsBuffer.SetData(m_fluidColors.Take(m_numParticles).ToArray());

            m_prepareFluidMaterial.SetBuffer("_Indices", m_indexBuffer);
            m_prepareFluidMaterial.SetBuffer("_Points", m_particleBuffer);
            m_prepareFluidMaterial.SetBuffer("_Anisotropy1", m_anisotropy1Buffer);
            m_prepareFluidMaterial.SetBuffer("_Anisotropy2", m_anisotropy2Buffer);
            m_prepareFluidMaterial.SetBuffer("_Anisotropy3", m_anisotropy3Buffer);
            m_prepareFluidMaterial.SetBuffer("_Colors", m_colorsBuffer);

            m_meshRender.material.SetBuffer("_Points", m_particleBuffer);
            m_meshRender.material.SetBuffer("_Indices", m_indexBuffer);
            m_meshRender.material.SetBuffer("_Anisotropy1", m_anisotropy1Buffer);
            m_meshRender.material.SetBuffer("_Anisotropy2", m_anisotropy2Buffer);
            m_meshRender.material.SetBuffer("_Anisotropy3", m_anisotropy3Buffer);

            m_temporaryBuffer = new Vector4[m_numParticles];
        }

        protected virtual void UpdateRenderResources()
        {
            // Notify cameras update
            foreach (var cam in m_cameraStates.Keys)
            {
                m_cameraStates[cam].NotifyDataChange();
            }

            // Update active indices
            m_totalActiveIndicesCount = 0;

            if (m_activeFluidIndices == null || m_activeFluidIndices.Length < m_maxNumActiveIndices)
            {
                m_activeFluidIndices = new int[m_maxNumActiveIndices];
                CreateComputeBuffer(ref m_indexBuffer, sizeof(int), m_maxNumActiveIndices);
            }

            if (m_particleBuffer == null || m_particleBuffer.count < m_numParticles)
            {
                CreateComputeBuffer(ref m_particleBuffer, 16, m_numParticles);
                CreateComputeBuffer(ref m_anisotropy1Buffer, sizeof(float) * 4, m_numParticles);
                CreateComputeBuffer(ref m_anisotropy2Buffer, sizeof(float) * 4, m_numParticles);
                CreateComputeBuffer(ref m_anisotropy3Buffer, sizeof(float) * 4, m_numParticles);
                CreateComputeBuffer(ref m_colorsBuffer, sizeof(float) * 4, m_numParticles);
                m_temporaryBuffer = new Vector4[m_numParticles];
            }

            bool shouldUpdateIndexBuffer = false;
            for (int i = 0; i < m_actors.Count; i++)
            {
                PhysxParticleActor actor = m_actors[i];
                if (actor.IndicesDirty)
                {
                    shouldUpdateIndexBuffer = true;
                    actor.IndicesDirty = false;
                }
                List<int> actorIndices = actor.ActiveParticleIndices;
                int actorIndicesCount = actorIndices.Count;

                actorIndices.CopyTo(0, m_activeFluidIndices, m_totalActiveIndicesCount, actorIndicesCount);
                m_totalActiveIndicesCount += actorIndicesCount;
            }

            if (shouldUpdateIndexBuffer)
            {
                m_indexBuffer.SetData(m_activeFluidIndices, 0, 0, m_indexBuffer.count);

                // Use compute shader for calculating the indices directly in the buffer.
                m_chunkAddShader.SetBuffer(0, "dataBuffer", m_indexBuffer);
                int startIndex = 0;
                for (int i = 0; i < m_actors.Count; i++)
                {
                    PhysxParticleActor actor = m_actors[i];
                    int actorIndicesCount = actor.ActiveParticleIndices.Count;
                    m_chunkAddShader.SetInt("valueToAdd", actor.ParticleData.IndexOffset);
                    m_chunkAddShader.SetInt("startIndex", startIndex);
                    m_chunkAddShader.SetInt("length", actorIndicesCount);

                    int threadGroups = Mathf.CeilToInt(m_maxNumActiveIndices / 256.0f);
                    m_chunkAddShader.Dispatch(0, threadGroups, 1, 1);
                    startIndex += actorIndicesCount;
                }
            }

            m_particleBuffer.SetData(m_sharedPositionInvMass.Take(m_numParticles).ToArray());
            if (m_fluidMaterial)
            {
                if (m_numParticles > 0)
                {
                    PxAnisotropyBuffer aniBuffer = Physx.GetAnisotropyAll(m_pbdParticleSystem.NativeObjectPtr);

                    CopyBufferContent(aniBuffer.anisotropy1, m_anisotropy1Buffer);
                    CopyBufferContent(aniBuffer.anisotropy2, m_anisotropy2Buffer);
                    CopyBufferContent(aniBuffer.anisotropy3, m_anisotropy3Buffer);

                    Physx.PBDParticleSystemGetBounds(m_pbdParticleSystem.NativeObjectPtr, out m_pxBounds3);
                }
                // recalculate bounds and center
                Vector3 center = (m_pxBounds3.minimum + m_pxBounds3.maximum) * 0.5f;
                Vector3 size = m_pxBounds3.maximum - m_pxBounds3.minimum;

                bool IsValidVector(Vector3 vec)
                {
                    return !float.IsNaN(vec.x) && !float.IsNaN(vec.y) && !float.IsNaN(vec.z) &&
                        !float.IsInfinity(vec.x) && !float.IsInfinity(vec.y) && !float.IsInfinity(vec.z);
                }

                // Check for validity of vectors
                if (!IsValidVector(center) || !IsValidVector(size))
                {
                    Debug.LogError("Invalid vector detected: NaN or Infinity present.");
                    center = Vector3.zero; // Default center
                    size = new Vector3(100, 100, 100); // Default size
                }

                m_bounds.center = center;
                m_bounds.size = size;
                m_bounds.Expand(m_pbdParticleSystem.ParticleSpacing * 2);
                m_meshRender.bounds = m_bounds;
            }
        }

        protected virtual void DestroyRenderResources()
        {
            m_initialized = false;
            foreach (var kvp in m_cameraRenderTextures)
            {
                kvp.Value.Release();
            }
            m_cameraRenderTextures.Clear();
            m_cameraStates.Clear();
            m_cameraCommands.Clear();
            GetComponent<MeshFilter>().mesh = null;
            GetComponent<MeshRenderer>().material = null;
            if (m_anisotropy1Buffer != null) { m_anisotropy1Buffer.Release(); m_anisotropy1Buffer = null; }
            if (m_anisotropy2Buffer != null) { m_anisotropy2Buffer.Release(); m_anisotropy2Buffer = null; }
            if (m_anisotropy3Buffer != null) { m_anisotropy3Buffer.Release(); m_anisotropy3Buffer = null; }
            if (m_mesh) DestroyImmediate(m_mesh);
            if (m_prepareFluidMaterial) DestroyImmediate(m_prepareFluidMaterial);
            if (m_particleBuffer != null) { m_particleBuffer.Release(); m_particleBuffer = null; }
            if (m_indexBuffer != null) { m_indexBuffer.Release(); m_indexBuffer = null; }
            if (m_colorsBuffer != null) { m_colorsBuffer.Release(); m_colorsBuffer = null; }
        }

        protected void CreateComputeBuffer(ref ComputeBuffer buffer, int stride, int count)
        {
            if (buffer != null && buffer.count != count)
            {
                buffer.Release();
                buffer = null;
            }

            if (buffer == null)
            {
                buffer = new ComputeBuffer(count, stride);
            }
        }

        protected void CopyBufferContent(IntPtr source, ComputeBuffer target)
        {
            PhysxUtils.FastCopy(source, m_temporaryBuffer);
            target.SetData(m_temporaryBuffer);
        }

        private void Awake()
        {
            if (!m_initialized)
            {
                CreateRenderResources();
                UpdateRenderResources();
                m_initialized = true;
            }
        }

        private void FixedUpdate()
        {
            if (m_initialized)
            {
                UpdateRenderResources();
            }
        }

        private void OnDisable()
        {
            DestroyRenderResources();
        }

        protected class CameraState
        {
            public Vector3 position;
            public Quaternion rotation;
            public Matrix4x4 projectionMatrix;

            public bool IsPoseOrDataChanged
            {
                get { return m_isPoseOrDataChanged; }
            }

            public bool IsProjectionChanged
            {
                get { return m_isProjectionChanged; }
            }

            public CameraState(Camera camera)
            {
                position = camera.transform.position;
                rotation = camera.transform.rotation;
                projectionMatrix = camera.projectionMatrix;
            }

            public void Update(Camera camera)
            {
                m_isPoseOrDataChanged |= CheckCameraPoseChanged(camera);
                m_isProjectionChanged |= CheckCameraProjectionChanged(camera);
                position = camera.transform.position;
                rotation = camera.transform.rotation;
                projectionMatrix = camera.projectionMatrix;
            }

            public void ResetHasChanged()
            {
                m_isPoseOrDataChanged = false;
                m_isProjectionChanged = false;
            }

            public void NotifyDataChange()
            {
                m_isPoseOrDataChanged = true;
            }

            private bool CheckCameraPoseChanged(Camera camera)
            {
                return position != camera.transform.position ||
                    rotation != camera.transform.rotation;
            }

            private bool CheckCameraProjectionChanged(Camera camera)
            {
                return projectionMatrix != camera.projectionMatrix;
            }

            private bool m_isPoseOrDataChanged = true;
            private bool m_isProjectionChanged = true;
        }

        protected class CameraRenderTextures
        {
            public RenderTexture depthTexture;
            public RenderTexture colorTexture;
            public RenderTexture depthBlurTexture;
            public RenderTexture depthTempTexture;

            public CameraRenderTextures(int width, int height)
            {
                depthTexture = new RenderTexture(new RenderTextureDescriptor(width, height)
                {
                    colorFormat = RenderTextureFormat.RFloat,
                    depthBufferBits = 24
                });

                colorTexture = new RenderTexture(new RenderTextureDescriptor(width, height)
                {
                    colorFormat = RenderTextureFormat.ARGB32,
                    depthBufferBits = 16 // To enable ZTest in the shader
                });

                depthBlurTexture = new RenderTexture(new RenderTextureDescriptor(width, height)
                {
                    colorFormat = RenderTextureFormat.RFloat,
                    depthBufferBits = 0
                });

                depthTempTexture = new RenderTexture(new RenderTextureDescriptor(width, height)
                {
                    colorFormat = RenderTextureFormat.RFloat,
                    depthBufferBits = 0
                });
            }

            public void Release()
            {
                if (depthTexture != null)
                {
                    depthTexture.Release();
                    Destroy(depthTexture);
                }

                if (colorTexture != null)
                {
                    colorTexture.Release();
                    Destroy(colorTexture);
                }

                if (depthBlurTexture != null)
                {
                    depthBlurTexture.Release();
                    Destroy(depthBlurTexture);
                }

                if (depthTempTexture != null)
                {
                    depthTempTexture.Release();
                    Destroy(depthTempTexture);
                }
            }
        }

        protected class CameraCommands
        {
            public CommandBuffer copyBackground;
            public CommandBuffer drawColor;
        }

        protected Dictionary<Camera, CameraState> m_cameraStates = new Dictionary<Camera, CameraState>();
        protected Dictionary<Camera, CameraRenderTextures> m_cameraRenderTextures = new Dictionary<Camera, CameraRenderTextures>();
        protected Dictionary<Camera, CameraCommands> m_cameraCommands = new Dictionary<Camera, CameraCommands>();
        private void RemoveCommandBuffer(Camera cam)
        {
            if (m_cameraCommands.ContainsKey(cam))
            {
                var cameraCommands = m_cameraCommands[cam];
                cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, cameraCommands.copyBackground);
                cam.RemoveCommandBuffer(CameraEvent.AfterDepthTexture, cameraCommands.drawColor);
                m_cameraCommands.Remove(cam);
                if (m_cameraCommands.Count == 0) Camera.onPostRender -= RemoveCommandBuffer;
            }
        }

        void OnWillRenderObject()
        {
            if (!m_initialized)
            {
                CreateRenderResources();
                UpdateRenderResources();
                m_initialized = true;
            }

            Camera cam = Camera.current;

            int colorPass = m_prepareFluidMaterial.FindPass("FluidColor".ToUpper());
            int depthPass = m_prepareFluidMaterial.FindPass("FluidDepth".ToUpper());
            int depthBlurPass = -1;
            int depthBlurPass1D = -1;
            if (m_depthFilterType == DepthFilterType.NarrowRange)
            {
                depthBlurPass = m_prepareFluidMaterial.FindPass("FluidDepthNarrowRangeFilter2D".ToUpper());
                depthBlurPass1D = m_prepareFluidMaterial.FindPass("FluidDepthNarrowRangeFilter1D".ToUpper());
            }
            else if (m_depthFilterType == DepthFilterType.Bilateral)
            {
                depthBlurPass = m_prepareFluidMaterial.FindPass("FluidDepthBilateralFilter".ToUpper());
            }

            if (!m_cameraStates.ContainsKey(cam))
            {
                cam.depthTextureMode = DepthTextureMode.Depth;
                m_cameraStates[cam] = new CameraState(cam);
            }
            m_cameraStates[cam].Update(cam);
            bool hasChanged = m_cameraStates[cam].IsPoseOrDataChanged || m_cameraStates[cam].IsProjectionChanged;
            // hasChanged = true; // for debug
            if (!m_cameraRenderTextures.ContainsKey(cam))
            {
                m_cameraRenderTextures[cam] = new CameraRenderTextures(cam.pixelWidth, cam.pixelHeight);
            }
            else if (m_cameraStates[cam].IsProjectionChanged)
            {
                m_cameraRenderTextures[cam].Release();
                m_cameraRenderTextures[cam] = new CameraRenderTextures(cam.pixelWidth, cam.pixelHeight);
            }
            CameraRenderTextures renderTextures = m_cameraRenderTextures[cam];
            RenderTexture active = RenderTexture.active;
            m_cameraStates[cam].ResetHasChanged();

            if (!m_cameraCommands.ContainsKey(cam))
            {
                // TODO: this is redundant with multiple PBD systems
                CommandBuffer copyBackground = new CommandBuffer
                {
                    name = "Copy fluid background"
                };
                int fluidBackgroundID = Shader.PropertyToID("_FluidBackground");
                copyBackground.GetTemporaryRT(fluidBackgroundID, -1, -1, 0);
                copyBackground.Blit(BuiltinRenderTextureType.CurrentActive, fluidBackgroundID);
                cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, copyBackground);

                CommandBuffer drawColor = new CommandBuffer
                {
                    name = "Draw fluid color"
                };
                if (hasChanged)
                {
                    drawColor.SetRenderTarget(renderTextures.colorTexture);
                    drawColor.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                    MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
                    materialPropertyBlock.SetTexture("_DepthTex", renderTextures.depthBlurTexture);
                    materialPropertyBlock.SetMatrix("_ViewMatrix", cam.worldToCameraMatrix);
                    materialPropertyBlock.SetMatrix("_ProjMatrix", GL.GetGPUProjectionMatrix(cam.projectionMatrix, active != null));
                    materialPropertyBlock.SetVector("_ScreenSize", new Vector2(cam.pixelWidth, cam.pixelHeight));
                    materialPropertyBlock.SetFloat("FLIP_Y", active ? 1.0f : 0.0f);
                    materialPropertyBlock.SetFloat("_ParticleDiameter", m_pbdParticleSystem.ParticleSpacing);
                    drawColor.DrawProcedural(Matrix4x4.identity, m_prepareFluidMaterial, colorPass, MeshTopology.Points, m_indexBuffer.count, 1, materialPropertyBlock);
                }
                cam.AddCommandBuffer(CameraEvent.AfterDepthTexture, drawColor);

                CameraCommands cameraCommands = new CameraCommands
                {
                    copyBackground = copyBackground,
                    drawColor = drawColor
                };

                m_cameraCommands.Add(cam, cameraCommands);
                if (m_cameraCommands.Count == 1) Camera.onPostRender += RemoveCommandBuffer;
            }

            if (hasChanged && depthPass != -1 && depthBlurPass != -1 && m_indexBuffer != null)
            {
                RenderTexture depth = renderTextures.depthTexture;
                Graphics.SetRenderTarget(depth);

                GL.Clear(false, true, new Color(cam.farClipPlane, 0, 0, 0), 1.0f);

                // resetting these buffers to fix the not rendering issue when refocusing on the window
                m_prepareFluidMaterial.SetInteger("_IndexCount", m_totalActiveIndicesCount);
                m_prepareFluidMaterial.SetBuffer("_Indices", m_indexBuffer);
                m_prepareFluidMaterial.SetBuffer("_Points", m_particleBuffer);
                m_prepareFluidMaterial.SetBuffer("_Anisotropy1", m_anisotropy1Buffer);
                m_prepareFluidMaterial.SetBuffer("_Anisotropy2", m_anisotropy2Buffer);
                m_prepareFluidMaterial.SetBuffer("_Anisotropy3", m_anisotropy3Buffer);
                m_prepareFluidMaterial.SetBuffer("_Colors", m_colorsBuffer);
                m_prepareFluidMaterial.SetMatrix("_ViewMatrix", cam.worldToCameraMatrix);
                m_prepareFluidMaterial.SetMatrix("_ProjMatrix", GL.GetGPUProjectionMatrix(cam.projectionMatrix, active != null));
                m_prepareFluidMaterial.SetPass(depthPass);
                Graphics.DrawProceduralNow(MeshTopology.Points, m_indexBuffer.count);

                RenderTexture depthBlur = renderTextures.depthBlurTexture;
                RenderTexture depthTemp = renderTextures.depthTempTexture;
                m_prepareFluidMaterial.SetFloat("_FarPlane", cam.farClipPlane);
                m_prepareFluidMaterial.SetVector("_InvScreen", new Vector2(1.0f / cam.pixelWidth, 1.0f / cam.pixelHeight));
                m_prepareFluidMaterial.SetInteger("_FixedFilterSize", 0);
                m_prepareFluidMaterial.SetFloat("_WorldFilterSize", m_filterWorldSize);
                m_prepareFluidMaterial.SetFloat("_ParticleRadius", m_pbdParticleSystem.ParticleSpacing / 2.0f);
                m_prepareFluidMaterial.SetInteger("_MaxFilterSize", 20);
                m_prepareFluidMaterial.SetFloat("_ThresholdRatio", m_filterThresholdRatio);
                m_prepareFluidMaterial.SetFloat("_ClampRatio", m_filterClampRatio);
                m_prepareFluidMaterial.SetFloat("_FOVRatio", cam.pixelHeight / 2.0f / 0.41421356f);
                if (m_depthFilterType == DepthFilterType.NarrowRange)
                {
                    if (m_narrowFilter1D && depthBlurPass1D != -1)
                    {
                        for (int i = 0; i < m_filterIterations; i++)
                        {
                            // Horizontal + vertical
                            Graphics.SetRenderTarget(depthTemp);
                            GL.Clear(false, true, new Color(cam.farClipPlane, 0, 0, 0));
                            m_prepareFluidMaterial.SetTexture("_DepthTex", depth);
                            m_prepareFluidMaterial.SetInteger("_FilterDirection", 0);
                            Graphics.Blit(null, m_prepareFluidMaterial, depthBlurPass1D);

                            Graphics.SetRenderTarget(depth);
                            GL.Clear(false, true, new Color(cam.farClipPlane, 0, 0, 0));
                            m_prepareFluidMaterial.SetTexture("_DepthTex", depthTemp);
                            m_prepareFluidMaterial.SetInteger("_FilterDirection", 1);
                            Graphics.Blit(null, m_prepareFluidMaterial, depthBlurPass1D);
                        }
                        // Clean-up pass with 2D fixed filter size
                        Graphics.SetRenderTarget(depthBlur);
                        GL.Clear(false, true, new Color(cam.farClipPlane, 0, 0, 0));
                        m_prepareFluidMaterial.SetInteger("_FixedFilterSize", m_filterCleanUpFixedSize);
                        m_prepareFluidMaterial.SetTexture("_DepthTex", depth);
                        Graphics.Blit(null, m_prepareFluidMaterial, depthBlurPass);
                    }
                }

                if (m_depthFilterType == DepthFilterType.Bilateral || (m_depthFilterType == DepthFilterType.NarrowRange && !m_narrowFilter1D))
                {
                    // Swap between depth and depthTemp for (iteration - 1) times
                    RenderTexture src = depth;
                    RenderTexture dest = depthTemp;
                    for (int i = 0; i < m_filterIterations - 1; i++)
                    {
                        Graphics.SetRenderTarget(dest);
                        GL.Clear(false, true, new Color(cam.farClipPlane, 0, 0, 0));
                        m_prepareFluidMaterial.SetTexture("_DepthTex", src);
                        Graphics.Blit(null, m_prepareFluidMaterial, depthBlurPass);

                        RenderTexture temp = src;
                        src = dest;
                        dest = temp;
                    }
                    // Lastly render to depthBlur
                    Graphics.SetRenderTarget(depthBlur);
                    GL.Clear(false, true, new Color(cam.farClipPlane, 0, 0, 0));
                    m_prepareFluidMaterial.SetTexture("_DepthTex", src);
                    Graphics.Blit(null, m_prepareFluidMaterial, depthBlurPass);
                }

                Graphics.SetRenderTarget(active);
            }

            m_meshRender.material.SetInteger("_IndexCount", m_totalActiveIndicesCount);
            m_meshRender.material.SetTexture("_DepthTex", renderTextures.depthBlurTexture);
            m_meshRender.material.SetTexture("_ColorTex", renderTextures.colorTexture);
            m_meshRender.material.SetFloat("FLIP_Y", active ? 1.0f : 0.0f);
            m_meshRender.material.SetFloat("FLIP_Y_2", active ? 1.0f : 0.0f);
            m_meshRender.material.SetFloat("LERP_BLEND", m_lerpBlend ? 1.0f : 0.0f);
        }

        [SerializeField]
        protected ComputeShader m_chunkAddShader;
        [SerializeField]
        protected Shader m_fluidShader;
        [SerializeField]
        protected Material m_fluidMaterial;
        [SerializeField]
        protected bool m_lerpBlend = false;
        [SerializeField]
        protected DepthFilterType m_depthFilterType = DepthFilterType.Bilateral;
        [SerializeField]
        protected bool m_narrowFilter1D = true;
        [SerializeField]
        protected int m_filterIterations = 1;
        [SerializeField]
        protected float m_filterWorldSize = 3;
        [SerializeField]
        protected float m_filterThresholdRatio = 1;
        [SerializeField]
        protected float m_filterClampRatio = 0.5f;
        [SerializeField]
        protected int m_filterCleanUpFixedSize = 3;

        protected int m_numParticles;
        protected PhysxPBDParticleSystem m_pbdParticleSystem;
        protected List<PhysxFluidActor> m_actors = new List<PhysxFluidActor>();
        protected int m_totalActiveIndicesCount;
        protected int m_maxNumActiveIndices;
        protected int[] m_activeFluidIndices;
        protected Mesh m_mesh;
        protected PxBounds3 m_pxBounds3 = new PxBounds3();
        protected Bounds m_bounds = new Bounds();

        protected Material m_prepareFluidMaterial;
        protected ComputeBuffer m_particleBuffer, m_indexBuffer, m_anisotropy1Buffer, m_anisotropy2Buffer, m_anisotropy3Buffer, m_colorsBuffer;
        protected Vector4[] m_temporaryBuffer = new Vector4[0];

        protected bool m_initialized = false;
        protected Vector4[] m_sharedPositionInvMass;
        protected Color[] m_fluidColors;
        protected MeshRenderer m_meshRender;
    }
}
