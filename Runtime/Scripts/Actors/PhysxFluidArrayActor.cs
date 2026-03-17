using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Actors/PhysX Fluid Array Actor")]
    public class PhysxFluidArrayActor : PhysxFluidActor
    {
        protected override void CreateNativeObject()
        {
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

        protected Vector4[] TransformParticles(Vector4[] particles)
        {
            Vector4[] o = new Vector4[particles.Length];
            Vector3 p;

            for (int i = 0; i < particles.Length; i++)
            {
                p = new Vector3(particles[i].x, particles[i].y, particles[i].z);
                p = transform.TransformPoint(p);
                o[i] = new Vector4(p.x, p.y, p.z, particles[i].w);
            }
            return o;
        }

        protected override void GenerateInitialParticles()
        {
            m_geometry = GetComponent<PhysxGeometry>();
            if (m_geometry == null)
            {
                Debug.LogError("Geometry is not assigned.");
                return;
            }

            if (m_geometry.NativeObjectPtr == IntPtr.Zero)
            {
                m_geometry.Recreate();
                if (m_geometry.NativeObjectPtr == IntPtr.Zero)
                {
                    Debug.LogError("Geometry native object is not initialized.");
                    return;
                }
            }

            float particleSpacing = m_pbdParticleSystem.ParticleSpacing;
            float particleInvMass = 1 / (m_density * 1.333f * 3.14159f * particleSpacing * particleSpacing * particleSpacing);

            PxBounds3 bounds;
            PxTransformData transformData = transform.ToPxTransformData();
            PhysxUtils.ComputeGeomBounds(out bounds, m_geometry.NativeObjectPtr, ref transformData, particleSpacing, 1);
            Vector3 min = bounds.minimum;
            Vector3 max = bounds.maximum;

            List<Vector4> particlePositions = new List<Vector4>();

            for (float x = min.x + particleSpacing * 0.5f; x <= max.x; x += particleSpacing)
            {
                for (float y = min.y + particleSpacing * 0.5f; y <= max.y; y += particleSpacing)
                {
                    for (float z = min.z + particleSpacing * 0.5f; z <= max.z; z += particleSpacing)
                    {
                        Vector3 point = new Vector3(x, y, z);
                        if (PhysxUtils.PointDistance(ref point, m_geometry.NativeObjectPtr, ref transformData) == 0)
                        {
                            particlePositions.Add(new Vector4(point.x, point.y, point.z, particleInvMass));
                        }
                    }
                }
            }
            m_initialParticlePositions = particlePositions.ToArray();
            m_numParticles = m_initialParticlePositions.Length;

            base.GenerateInitialParticles();
        }

        [SerializeField]
        private PhysxGeometry m_geometry;
    }
}
