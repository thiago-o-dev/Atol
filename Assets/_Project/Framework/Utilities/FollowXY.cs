using UnityEngine;

public class FollowXY : MonoBehaviour
{
    public Transform ReferenceTransform;
    void FixedUpdate()
    {
        if (ReferenceTransform != null)
            transform.position = new Vector3(ReferenceTransform.position.x, transform.position.y, ReferenceTransform.position.z);
    }
}
