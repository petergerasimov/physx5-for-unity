using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysX5ForUnity
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public abstract class PhysxParticleActor : PhysxActor
    {
        public PhysxPBDParticleSystem PBDParticleSystem
        {
            get { return m_pbdParticleSystem; }
        }

        public virtual int NumParticles
        {
            get { return m_numParticles; }
        }

        public ParticleData ParticleData
        {
            get { return m_particleData; }
            set { m_particleData = value; }
        }

        public PhysxPBDMaterial PBDMaterial
        {
            get { return m_pbdMaterial; }
        }

        public List<int> ActiveParticleIndices
        {
            get { return m_activeParticleIndices; }
        }

        public bool IndicesDirty
        {
            get { return m_indicesDirty; }
            set { m_indicesDirty = value; }
        }

        public List<ParticleRigidFilterPair> ParticleRigidFilterPairs
        {
            // A copy of the particle-rigid filter pairs list
            get { return new List<ParticleRigidFilterPair>(m_particleRigidFilterPairs); }
        }

        public void AddParticleRigidFilter(PhysxNativeGameObjectBase rigidActor, int particleIndex)
        {
            m_particleRigidFilterPairs.Add(new ParticleRigidFilterPair(particleIndex, rigidActor));
            Physx.AddParticleRigidFilter(m_nativeObjectPtr, rigidActor.NativeObjectPtr, particleIndex);
            if (rigidActor is PhysxActor actor)
            {
                actor.OnBeforeDestroy -= RemoveParticleRigidFilters;
                actor.OnBeforeDestroy += RemoveParticleRigidFilters;
            }
            else if (rigidActor is PhysxArticulationLinkBase link)
            {
                link.ArticulationKinematicTree.OnBeforeDestroy -= RemoveParticleRigidFilters;
                link.ArticulationKinematicTree.OnBeforeDestroy += RemoveParticleRigidFilters;
            }
        }

        public void RemoveParticleRigidFilter(ParticleRigidFilterPair particleRigidFilterPair)
        {
            m_particleRigidFilterPairs.Remove(particleRigidFilterPair);
            Physx.RemoveParticleRigidFilter(m_nativeObjectPtr, particleRigidFilterPair.RigidActor.NativeObjectPtr, particleRigidFilterPair.ParticleIndex);
        }

        public void RemoveParticleRigidFilters()
        {
            if (m_particleRigidFilterPairs.Count == 0) return;
            foreach (ParticleRigidFilterPair pair in m_particleRigidFilterPairs)
            {
                Physx.RemoveParticleRigidFilter(m_nativeObjectPtr, pair.RigidActor.NativeObjectPtr, pair.ParticleIndex);
            }
            m_particleRigidFilterPairs.Clear();
        }

        protected void FixedUpdate()
        {
            UpdateParticleData();
            UpdateRenderResources();
        }

        protected virtual void CreateRenderResources()
        {
            if (m_renderParticles)
            {
                CreateDrawParticlesRenderResources();
            }
        }

        protected virtual void UpdateRenderResources()
        {
            if (m_renderParticles)
            {
                UpdateDrawParticlesRenderResources();
            }
        }

        protected virtual void DestroyRenderResources()
        {
            if (m_renderParticles)
            {
                DestroyDrawParticlesRenderResources();
            }
        }

        private void CreateDrawParticlesRenderResources()
        {
            CreateComputeBuffer(ref m_particleBuffer, 16);
            CreateComputeBuffer(ref m_indexBuffer, sizeof(int));

            m_drawParticlesMesh = new Mesh
            {
                name = "Draw Particles",
                vertices = new Vector3[1],
                subMeshCount = 1
            };
            m_drawParticlesMesh.SetIndices(new int[0], MeshTopology.Points, 0);
            m_drawParticlesMesh.SetIndices(new int[m_numParticles], MeshTopology.Points, 0);

            m_indexBuffer.SetData(m_activeParticleIndices);

            m_particleMaterial = new Material(m_particleShader)
            {
                color = m_particleColor,
                hideFlags = HideFlags.HideAndDontSave
            };

            m_particleMaterial.SetBuffer("_Points", m_particleBuffer);
            m_particleMaterial.SetBuffer("_Indices", m_indexBuffer);
            m_particleMaterial.SetFloat("_Radius", m_pbdParticleSystem.ParticleSpacing / 2);
            m_particleMaterial.SetColor("_Color", m_particleColor);

            GetComponent<MeshRenderer>().material = m_particleMaterial;
            GetComponent<MeshFilter>().mesh = m_drawParticlesMesh;
        }

        private void UpdateDrawParticlesRenderResources()
        {
            m_particleBuffer.SetData(m_particleData.PositionInvMass.ToArray());
            m_indexBuffer.SetData(m_activeParticleIndices);
        }

        private void DestroyDrawParticlesRenderResources()
        {
            if (m_particleBuffer != null)
            {
                m_particleBuffer.Release();
                m_particleBuffer = null;
            }
            if (m_drawParticlesMesh) DestroyImmediate(m_drawParticlesMesh);
            if (m_particleMaterial) DestroyImmediate(m_particleMaterial);

            if (m_indexBuffer != null)
            {
                m_indexBuffer.Release();
                m_indexBuffer = null;
            }
        }

        protected virtual void UpdateParticleData()
        {
            SyncParticleDataGet();
        }

        protected override void CreateNativeObject()
        {
            CreateRenderResources();
            SyncParticleDataGet();
            UpdateRenderResources();
        }

        protected override void DestroyNativeObject()
        {
            RemoveParticleRigidFilters();
            m_particleData = null;
            m_nativeObjectPtr = IntPtr.Zero;
            DestroyRenderResources();
            RemoveFromDependencies();
        }

        protected override void EnableActor()
        {
            if (m_nativeObjectPtr == IntPtr.Zero) CreateActor();
            m_pbdParticleSystem.EnableActor(this);
            Physx.AddPBDObjectToParticleSystem(m_nativeObjectPtr);
        }

        protected override void DisableActor()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) Physx.RemovePBDObjectFromParticleSystem(m_nativeObjectPtr);
            m_pbdParticleSystem.DisableActor(this);
        }

        protected virtual void GenerateInitialParticles()
        {
            m_activeParticleIndices = new List<int>(m_numParticles);
            for (int i = 0; i < m_numParticles; i++)
            {
                m_activeParticleIndices.Add(i);
            }
            m_indicesDirty = true;
        }

        protected void AddToDependencies()
        {
            m_pbdMaterial.AddActor(this);
            m_pbdParticleSystem.AddActor(this);
        }

        protected void RemoveFromDependencies()
        {
            m_pbdParticleSystem.RemoveActor(this);
            m_pbdMaterial.RemoveActor(this);
        }

        protected void CreateComputeBuffer(ref ComputeBuffer buffer, int size)
        {
            if (buffer != null && buffer.count != m_numParticles)
            {
                buffer.Release();
                buffer = null;
            }

            if (buffer == null)
            {
                buffer = new ComputeBuffer(m_numParticles, size);
            }
        }

        protected void SyncParticleDataGet()
        {
            if (m_nativeObjectPtr == IntPtr.Zero) return;
            m_particleData.SyncParticlesGet();
        }

        [SerializeField]
        protected PhysxPBDParticleSystem m_pbdParticleSystem;
        [SerializeField]
        protected PhysxPBDMaterial m_pbdMaterial;
        [SerializeField]
        protected bool m_renderParticles = false;
        [SerializeField]
        private Shader m_particleShader;

        // For rendering the particles
        private ComputeBuffer m_particleBuffer;
        private ComputeBuffer m_indexBuffer;
        private Mesh m_drawParticlesMesh;
        private Material m_particleMaterial;
        private Color m_particleColor = new Color(0.5f, 0.5f, 0.5f);  // Default color for particles

        protected ParticleData m_particleData;
        protected int m_numParticles = 1000;
        protected List<int> m_activeParticleIndices;
        protected bool m_indicesDirty = true;
        protected List<ParticleRigidFilterPair> m_particleRigidFilterPairs = new List<ParticleRigidFilterPair>();
    }
}
