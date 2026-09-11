using TankWarfare.Network;
using UnityEngine;

namespace TankWarfare.Gameplay
{
    internal sealed class BulletView : MonoBehaviour
    {
        private const float MaximumCorrection = 2.5f;
        private const float PredictionLifetime = 0.75f;

        private Vector3 velocity;
        private float age;

        public bool IsPredicted { get; private set; }
        public bool IsExpiredPrediction => IsPredicted && age >= PredictionLifetime;

        public void Initialize(Vector3 position, float yaw, float speed, bool predicted)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            SetMotion(yaw, speed);
            IsPredicted = predicted;
            age = 0f;
        }

        public void Confirm(BulletSnapshot snapshot)
        {
            Vector3 authoritative = new Vector3(snapshot.x, transform.position.y, snapshot.z);
            Vector3 error = authoritative - transform.position;

            if (error.sqrMagnitude > MaximumCorrection * MaximumCorrection)
            {
                transform.position = authoritative;
            }
            else
            {
                Vector3 forward = velocity.sqrMagnitude > 0.001f ? velocity.normalized : transform.forward;
                Vector3 sidewaysError = error - Vector3.Project(error, forward);
                float forwardError = Vector3.Dot(error, forward);
                transform.position += sidewaysError * 0.65f + forward * (forwardError * 0.08f);
            }

            transform.rotation = Quaternion.Euler(0f, snapshot.yaw, 0f);
            SetMotion(snapshot.yaw, snapshot.speed);
            IsPredicted = false;
            age = 0f;
        }

        private void Update()
        {
            age += Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
        }

        private void SetMotion(float yaw, float speed)
        {
            if (speed <= 0f) speed = 12f;
            float radians = yaw * Mathf.Deg2Rad;
            velocity = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * speed;
        }
    }
}
