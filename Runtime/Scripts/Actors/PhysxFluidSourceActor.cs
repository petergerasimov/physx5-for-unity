using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Actors/PhysX Fluid Source Actor")]
    public class PhysxFluidSourceActor : PhysxFluidActor
    {
        public Mesh NozzleSurface
        {
            get { return m_nozzleSurface; }
        }

        public float NozzleDensity
        {
            get { return m_nozzleDensity; }
        }

        public bool IsInactiveParticlePositionGlobal
        {
            get { return m_isInactiveParticlePositionGlobal; }
        }

        public Vector3 InactiveParticlePosition
        {
            get { return m_inactiveParticlePosition; }
        }

        public float StartSpeed
        {
            get { return m_startSpeed; }
            set { m_startSpeed = value; }
        }

        public override void ResetObject()
        {
            base.ResetObject();
            m_activeParticleIndices.Clear();
            m_indicesDirty = true;
        }

        protected override void CreateNativeObject()
        {
            m_lastSpawnTravelVelocity = m_startSpeed;
            GenerateNozzleData();
            GenerateInitialParticles();
            AddToDependencies();
            if (m_scene != null && m_pbdMaterial != null && m_scene.NativeObjectPtr != IntPtr.Zero && ParticleData.NativeParticleObjectPtr == IntPtr.Zero)
            {
                ParticleData.NativeParticleObjectPtr = Physx.CreateFluid(m_scene.NativeObjectPtr, m_pbdParticleSystem.NativeObjectPtr, m_pbdMaterial.NativeObjectPtr,
                    ref m_initialParticlePositions[0], m_numParticles, m_pbdParticleSystem.ParticleSpacing, m_density, 1000, m_buoyancy);
                m_nativeObjectPtr = ParticleData.NativeParticleObjectPtr;
            }
            base.CreateNativeObject();
        }

        protected override void DestroyNativeObject()
        {
            if (m_scene != null && m_scene.NativeObjectPtr != IntPtr.Zero && ParticleData != null && ParticleData.NativeParticleObjectPtr != IntPtr.Zero)
            {
                Physx.ReleaseParticleSystemObject(ParticleData.NativeParticleObjectPtr);
                ParticleData.NativeParticleObjectPtr = IntPtr.Zero;
            }
            base.DestroyNativeObject();
        }

        protected override void GenerateInitialParticles()
        {
            m_numParticles = m_maxNumParticles;
            float particleSpacing = m_pbdParticleSystem.ParticleSpacing;
            m_particleMass = 1 / (m_density * 1.333f * 3.14159f * particleSpacing * particleSpacing * particleSpacing);

            List<Vector4> particlePositions = new List<Vector4>();
            for (int i = 0; i < m_numParticles; i++)
            {
                particlePositions.Add(new Vector4(m_inactiveParticlePosition.x, m_inactiveParticlePosition.y, m_inactiveParticlePosition.z, 0));
            }
            m_initialParticlePositions = particlePositions.ToArray();
            m_numParticles = m_initialParticlePositions.Length;

            m_activeParticleIndices = new List<int>(m_numParticles); // No active indices at the beginning
        }

        protected override void UpdateParticleData()
        {
            base.UpdateParticleData();
            int numActiveParticles = m_activeParticleIndices.Count;
            if (numActiveParticles >= m_numParticles) return;
            float dT = Time.fixedDeltaTime;
            m_lastSpawnTravelVelocity += m_averageAccel * dT;
            m_lastSpawnTravelDist += m_lastSpawnTravelVelocity * dT;

            Span<Vector3> nozzleGlobalPositons = m_nozzleGlobalPositons.Span;
            Span<Vector3> nozzleGlobalDirections = m_nozzleGlobalDirections.Span;
            if (transform.hasChanged) UpdateNozzleGlobalPose();

            while (numActiveParticles < m_numParticles && m_lastSpawnTravelDist >= m_pbdParticleSystem.ParticleSpacing - 1e-6)
            {
                m_lastSpawnTravelDist = m_lastSpawnTravelDist - m_pbdParticleSystem.ParticleSpacing;
                m_lastSpawnTravelVelocity = Mathf.Sqrt(m_startSpeed * m_startSpeed + 2 * m_averageAccel * m_lastSpawnTravelDist);
                for (int i = 0; i < nozzleGlobalPositons.Length; i++)
                {
                    Vector3 nozzle = nozzleGlobalPositons[i];
                    Vector3 nozzleDirection = nozzleGlobalDirections[i];
                    Vector3 spawnLocation = nozzleDirection * m_lastSpawnTravelDist + nozzle;
                    Vector3 startVelocity = nozzleDirection * m_lastSpawnTravelVelocity;
                    m_particleData.SetParticle(numActiveParticles, new Vector4(spawnLocation.x, spawnLocation.y, spawnLocation.z, m_particleMass), false);
                    m_particleData.SetVelocity(numActiveParticles, startVelocity, false);
                    m_activeParticleIndices.Add(numActiveParticles);
                    m_indicesDirty = true;
                    
                    numActiveParticles++;
                    if (numActiveParticles >= m_numParticles) break;
                }

                m_particleData.SyncParticlesSet(true);
            }
        }

        private void GenerateNozzleData()
        {
            List<Vector3> nozzleLocalPositons = new List<Vector3>();
            List<Vector3> nozzleLocalDirections = new List<Vector3>();
            if (!m_isInactiveParticlePositionGlobal) m_inactiveParticlePosition = transform.TransformPoint(m_inactiveParticlePosition);
            if (m_nozzleSurface)
            {
                float scale;
                if (Mathf.Approximately(transform.lossyScale.x, transform.lossyScale.y) && Mathf.Approximately(transform.lossyScale.x, transform.lossyScale.z))
                {
                    scale = transform.lossyScale.x;
                }
                else
                {
                    Debug.LogWarning("Object scale is non-uniform. Fluid nozzle positions will not be uniform.");
                    scale = Mathf.Min(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
                }
                MeshDiscretizer.GetDiscretizedPointsOnMesh(out nozzleLocalPositons, out nozzleLocalDirections, m_nozzleSurface, m_pbdParticleSystem.ParticleSpacing / m_nozzleDensity, scale, m_pbdParticleSystem.ParticleSpacing / 2);
            }
            Vector3 sum = Vector3.zero;

            foreach (Vector3 direction in nozzleLocalDirections)
            {
                sum += direction;
            }
            m_averageLocalNozzleDirection = sum.normalized;

            m_nozzleLocalPositons = nozzleLocalPositons.ToArray().AsMemory();
            m_nozzleLocalDirections = nozzleLocalDirections.ToArray().AsMemory();
            m_nozzleGlobalPositons = nozzleLocalPositons.ToArray().AsMemory();
            m_nozzleGlobalDirections = nozzleLocalDirections.ToArray().AsMemory();
            UpdateNozzleGlobalPose();
        }

        private void UpdateNozzleGlobalPose()
        {
            m_nozzleLocalPositons.CopyTo(m_nozzleGlobalPositons);
            m_nozzleLocalDirections.CopyTo(m_nozzleGlobalDirections);
            transform.TransformPoints(m_nozzleGlobalPositons.Span);
            transform.TransformDirections(m_nozzleGlobalDirections.Span);
            m_averageGlobalNozzleDirection = transform.TransformDirection(m_averageLocalNozzleDirection);
            m_averageAccel = Vector3.Dot(m_averageGlobalNozzleDirection, m_scene.Gravity);
        }

        [SerializeField]
        private Mesh m_nozzleSurface = null;
        [SerializeField]
        private float m_nozzleDensity = 1;
        [SerializeField]
        private int m_maxNumParticles = 1000;
        [SerializeField]
        private bool m_isInactiveParticlePositionGlobal;
        [SerializeField]
        private Vector3 m_inactiveParticlePosition;
        [SerializeField]
        private float m_startSpeed;
        private float m_lastSpawnTravelDist;
        private float m_lastSpawnTravelVelocity;
        private float m_particleMass;
        private Memory<Vector3> m_nozzleLocalPositons;
        private Memory<Vector3> m_nozzleLocalDirections;
        private Vector3 m_averageLocalNozzleDirection; // For approximating the spawn particle travel distance and velocity. Works best if the nozzle directions do not vary too much.
        private Memory<Vector3> m_nozzleGlobalPositons;
        private Memory<Vector3> m_nozzleGlobalDirections;
        private Vector3 m_averageGlobalNozzleDirection; // For approximating the spawn particle travel distance and velocity. Works best if the nozzle directions do not vary too much.
        private float m_averageAccel = 0;
    }
}
