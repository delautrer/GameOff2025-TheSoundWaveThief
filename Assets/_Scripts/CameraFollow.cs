using UnityEngine;

public class CameraFollow : MonoBehaviour
{

    public Transform target;

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        float targetX = target.position.x;
        float targetY = target.position.y;

        float cameraZ = transform.position.z;

        transform.position = new Vector3(targetX, targetY, cameraZ);
    }
}