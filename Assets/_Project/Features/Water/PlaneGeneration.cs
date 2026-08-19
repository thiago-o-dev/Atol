using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(Material))]
public class PlaneGeneration : MonoBehaviour
{
    public int Size = 10;
    public float Scale = 1.0f;

    private Mesh _mesh;

    private Vector3[] _vertices;
    private int[] _triangles;
    private Vector2[] _uvs;
    private int _verticesLenght;

    [SerializeField] private bool _isUpdatingOnCPU = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _mesh = new();

        GetComponent<MeshFilter>().mesh = _mesh;

        _verticesLenght = (Size + 1) * (Size + 1);

        CreateOrUpdatePlane();
        UpdateMesh();
    }

    private void FixedUpdate()
    {

        if (_isUpdatingOnCPU)
        {
            CreateOrUpdatePlane();
            UpdateMesh();
        }
    }

    void CreateOrUpdatePlane()
    {
        _vertices = new Vector3[_verticesLenght];
        _uvs = new Vector2[_vertices.Length];

        float halfSizeX = (Scale * Size) / 2;
        float halfSizeZ = (Scale * Size) / 2;

        int i = 0;
        for (int z = 0; z <= Size; z++)
        {
            for (int x = 0; x <= Size; x++)
            {
                float xPos = (x * Scale) - halfSizeX;
                float zPos = (z * Scale) - halfSizeZ;
                float yPos = 0;

                _vertices[i] = new Vector3(xPos, yPos, zPos);

                if (_isUpdatingOnCPU)
                    _vertices[i] += WaterController.Instance.GetWaveAddition(_vertices[i] + transform.position, Time.timeSinceLevelLoad);

                _uvs[i] = new Vector2(_vertices[i].x, _vertices[i].z);
                i++;
            }
        }

        _triangles = new int[Size * Size * 6];

        int vert = 0;
        int tris = 0;

        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                _triangles[tris + 0] = vert + 0;
                _triangles[tris + 1] = vert + Size + 1;
                _triangles[tris + 2] = vert + 1;
                _triangles[tris + 3] = vert + 1;
                _triangles[tris + 4] = vert + Size + 1;
                _triangles[tris + 5] = vert + Size + 2;

                vert++;
                tris += 6;
            }
            vert++;
        }
    }
    void UpdateMesh()
    {
        _mesh.Clear();
        _mesh.vertices = _vertices;
        _mesh.triangles = _triangles;
        _mesh.uv = _uvs;
        _mesh.RecalculateNormals();
    }

    // Update is called once per frame
    void Update()
    {

    }
}
