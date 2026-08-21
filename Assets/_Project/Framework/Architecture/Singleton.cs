using UnityEngine;

namespace Assets._Project.Framework.Architecture
{
    public class Singleton<T> : MonoBehaviour where T : Component
    {
        protected static T _instance;

        public static T Instance => _instance;

        protected virtual bool IsPersistent => false;

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                var objs = FindObjectsByType<T>();

                if (objs.Length > 0)
                    _instance = objs[0];

                if (objs.Length > 1)
                    Debug.LogError($"There is more than one {typeof(T).Name} in the scene.");

                if (_instance == null)
                {
                    var obj = new GameObject($"_{typeof(T).Name}");
                    _instance = obj.AddComponent<T>();
                }

                if (IsPersistent)
                    DontDestroyOnLoad(_instance.gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }

            SingletonAwake();
        }

        protected virtual void SingletonAwake()
        {
            return;
        }
    }
}
