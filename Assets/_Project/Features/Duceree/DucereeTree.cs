using Assets._Project.Framework.Architecture;
using System.Collections;
using UnityEngine;

namespace Assets._Project.Features.Duceree
{
    // A árvore onde o player sempre renasce. Cada morte acende mais uma estrela nos galhos,
    // então ela guarda a contagem de mortes da tentativa e fica mais bonita a cada uma.
    public sealed class DucereeTree : Singleton<DucereeTree>
    {
        [Tooltip("Estrelas dos galhos, na ordem em que acendem.")]
        public Transform[] Stars;
        public float LightTime = 1.6f;
        [Tooltip("Com todas acesas, quanto cada nova morte ainda aumenta o brilho.")]
        public float ExtraGlow = 1.12f;

        public int Deaths { get; private set; }

        protected override void SingletonAwake()
        {
            foreach (Transform star in Stars)
                star.gameObject.SetActive(false);
        }

        public void LightStar()
        {
            if (Deaths < Stars.Length)
            {
                StartCoroutine(Light(Stars[Deaths]));
            }
            else
            {
                foreach (Transform star in Stars)
                    star.localScale *= ExtraGlow;
            }

            Deaths++;
        }

        private IEnumerator Light(Transform star)
        {
            Vector3 size = star.localScale;

            star.localScale = Vector3.zero;
            star.gameObject.SetActive(true);

            for (float t = 0f; t < 1f; t += Time.deltaTime / LightTime)
            {
                // Passa um pouco do tamanho e assenta, como uma chama pegando.
                star.localScale = size * (Mathf.SmoothStep(0f, 1f, t) + 0.35f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }

            star.localScale = size;
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;

            if (camera == null)
                return;

            foreach (Transform star in Stars)
            {
                if (star.gameObject.activeSelf)
                    star.rotation = camera.transform.rotation;
            }
        }
    }
}
