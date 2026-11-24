using UnityEngine;

public class AutoDestroy : MonoBehaviour
{
    [Tooltip("Time in seconds before the GameObject is destroyed.")]
    public float lifetime = 3.0f;

    [Tooltip("If true, destroys only this script component instead of the entire GameObject.")]
    public bool destroyComponentOnly = false;

    void Start()
    {
        if (destroyComponentOnly)
        {
            Destroy(this, lifetime);
        }
        else
        {
            Destroy(gameObject, lifetime);
        }
    }
}