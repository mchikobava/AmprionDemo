using System.Collections;
using UnityEngine;

namespace GorillaZilla
{
    public class BuildingPiece : MonoBehaviour
    {
        private DestructableBuilding _building;
        private Rigidbody rb;
        private MeshCollider col;
        private WaitForSeconds waiter = new(5);
        
        private bool shouldShrink = false;
        private float shrinkSpeed = 1f;
        private bool hasCrumbled = false;
        
        public void Setup(DestructableBuilding building)
        {
            _building = building;
            col = gameObject.AddComponent<MeshCollider>();
            col.convex = true;
            col.providesContacts = true;
            col.isTrigger = false;

            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }
        public void CrumblePiece(Vector3 hitForce)
        {
            EnablePhysics(hitForce);
            ShrinkAndDestroy();
            hasCrumbled = true;
        }

        private void EnablePhysics(Vector3 force)
        {
            rb.isKinematic = false;
            col.isTrigger = false;
            rb.AddForce(force);
        }
        private void ShrinkAndDestroy()
        {
            shouldShrink = true;
            StartCoroutine(HideOnDelay());
        }

        private IEnumerator HideOnDelay()
        {
            yield return waiter;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            //Shrink the building piece when collided with
            if (shouldShrink)
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * .2f, Time.deltaTime * shrinkSpeed);
        }
        
        private void OnCollisionEnter(Collision collision)
        {
            if (hasCrumbled) return;
            //On collision tell the entire building to handle collision logic (to remove floating pieces)
            if (collision.gameObject.layer != gameObject.layer)
            {
                _building.OnPieceCollision(this, collision);
            }
        }
        public float GetMaxHeight()
        {
            return col.bounds.max.y;
        }
        public float GetMinHeight()
        {
            return col.bounds.max.y;
        }
    }
}
