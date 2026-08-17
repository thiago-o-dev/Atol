using UnityEngine;

namespace Assets._Project.Framework.Architecture
{
    public class PersistentSingleton<T> : Singleton<T>
        where T : Component
    {
        protected override bool IsPersistent => true;
    }
}