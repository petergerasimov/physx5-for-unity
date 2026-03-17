using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Geometries/PhysX Triangle Geometry")]
    public class PhysxTriangleMeshGeometry : PhysxGeometry
    {
        public bool IsConvex
        {
            get { return m_isConvex; }
        }

        public Mesh Mesh
        {
            get { return m_mesh; }
            set { m_mesh = value; }
        }

        protected override void CreateGeometry()
        {
            if (m_mesh == null)
            {
                throw new InvalidOperationException("Mesh is not set.");
            }

            Vector3[] vertices = m_mesh.vertices;
            if (vertices.Length == 0)
            {
                throw new InvalidOperationException("Mesh has no vertices.");
            }
            int[] indices = m_mesh.triangles;
            float[] shapeParams = new float[3];
            shapeParams[0] = transform.lossyScale.x;
            shapeParams[1] = transform.lossyScale.y;
            shapeParams[2] = transform.lossyScale.z;

            if (m_isConvex)
            {
                m_pxMesh = PhysxUtils.CreateConvexMesh(vertices.Length, ref vertices[0], true, 64);
                m_nativeObjectPtr = PhysxUtils.CreatePxGeometry(PxGeometryType.ConvexMesh, 3, ref shapeParams[0], m_pxMesh);
            }
            else
            {
                m_pxMesh = PhysxUtils.CreateBV33TriangleMesh(vertices.Length, ref vertices[0], indices.Length / 3, ref indices[0], false, false, true, false, false, m_buildGpuData, m_sdfSpacing, m_sdfSubgridSize, m_bitsPerSdfSubgridPixel);
                m_nativeObjectPtr = PhysxUtils.CreatePxGeometry(PxGeometryType.TriangleMesh, 3, ref shapeParams[0], m_pxMesh);
            }
        }

        protected override void DestroyGeometry()
        {
            if (m_pxMesh != IntPtr.Zero) Physx.ReleaseMesh(m_pxMesh);
            base.DestroyGeometry();
        }

        private void OnDrawGizmosSelected()
        {
            if (enabled && m_drawWireFrame && m_mesh != null)
            {
                Gizmos.color = Color.yellow;

                // Apply the object's transformation to the mesh vertices
                Matrix4x4 matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

                // Draw the wireframe mesh
                for (int i = 0; i < m_mesh.triangles.Length; i += 3)
                {
                    Vector3 v1 = matrix.MultiplyPoint3x4(m_mesh.vertices[m_mesh.triangles[i]]);
                    Vector3 v2 = matrix.MultiplyPoint3x4(m_mesh.vertices[m_mesh.triangles[i + 1]]);
                    Vector3 v3 = matrix.MultiplyPoint3x4(m_mesh.vertices[m_mesh.triangles[i + 2]]);

                    Gizmos.DrawLine(v1, v2);
                    Gizmos.DrawLine(v2, v3);
                    Gizmos.DrawLine(v3, v1);
                }
            }
        }

        protected override string GenerateUniqueKey()
        {
            string key;
            if (m_sdfSpacing == 0)
            {
                // No SDF
                key = $"g_trimesh_{m_mesh.GetInstanceID()}_{m_isConvex}_{m_buildGpuData}";
            }
            else
            {
                key = $"g_trimesh_{m_mesh.GetInstanceID()}_{m_isConvex}_{m_buildGpuData}_{m_sdfSpacing}_{m_sdfSubgridSize}_{m_bitsPerSdfSubgridPixel}";
            }
            return key;
        }

        [SerializeField]
        private Mesh m_mesh;
        [SerializeField]
        private bool m_isConvex = false;
        [SerializeField]
        private bool m_buildGpuData = true;
        [SerializeField]
        private float m_sdfSpacing = 0.0f;
        [SerializeField]
        private int m_sdfSubgridSize = 6;
        [SerializeField]
        private PxSdfBitsPerSubgridPixel m_bitsPerSdfSubgridPixel = PxSdfBitsPerSubgridPixel.Bit8PerPixel;
        [SerializeField]
        private bool m_drawWireFrame = false;

        private IntPtr m_pxMesh = IntPtr.Zero;
    }
}
