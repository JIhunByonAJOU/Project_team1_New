using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace Gley.UrbanSystem
{
    /// <summary>
    /// This class is for testing purpose only
    /// It is the car controller provided by Unity:
    /// https://docs.unity3d.com/Manual/WheelColliderTutorial.html
    /// </summary>
    [System.Serializable]
    public class AxleInfo
    {
        public WheelCollider leftWheel;
        public WheelCollider rightWheel;
        public bool motor;
        public bool steering;
    }


    public class PlayerCar : MonoBehaviour
    {
        public List<AxleInfo> axleInfos;
        public Transform centerOfMass;
        public float maxMotorTorque;
        public float maxSteeringAngle;
        IVehicleLightsComponent lightsComponent;
        bool mainLights;
        bool brake;
        bool reverse;
        bool blinkLeft;
        bool blinkRifgt;
        float realtimeSinceStartup;
        Rigidbody rb;

        IUIInput inputScript;


        private void Start()
        {
            GetComponent<Rigidbody>().centerOfMass = centerOfMass.localPosition;
#if ENABLE_LEGACY_INPUT_MANAGER
            inputScript = gameObject.AddComponent<UIInputOld>().Initialize();
#else
            inputScript = gameObject.AddComponent<UIInputNew>().Initialize();
#endif
            lightsComponent = gameObject.GetComponent<VehicleLightsComponent>();
            lightsComponent.Initialize();
            rb = GetComponent<Rigidbody>();
        }

        // finds the corresponding visual wheel
        // correctly applies the transform
        public void ApplyLocalPositionToVisuals(WheelCollider collider)
        {
            if (collider.transform.childCount == 0)
            {
                return;
            }

            Transform visualWheel = collider.transform.GetChild(0);

            Vector3 position;
            Quaternion rotation;
            collider.GetWorldPose(out position, out rotation);

            visualWheel.transform.position = position;
            visualWheel.transform.rotation = rotation;
        }

        public void FixedUpdate()
        {
            float motor = maxMotorTorque * inputScript.GetVerticalInput();
            float steering = maxSteeringAngle * inputScript.GetHorizontalInput();
#if UNITY_6000_0_OR_NEWER
            var velocity = rb.linearVelocity;
#else
            var velocity = rb.velocity;
#endif
            float localVelocity = transform.InverseTransformDirection(velocity).z + 0.1f;
            reverse = false;
            brake = false;
            if (localVelocity < 0)
            {
                reverse = true;
            }

            if (motor < 0)
            {
                if (localVelocity > 0)
                {
                    brake = true;
                }
            }
            else
            {
                if (motor > 0)
                {
                    if (localVelocity < 0)
                    {
                        brake = true;
                    }
                }
            }

            foreach (AxleInfo axleInfo in axleInfos)
            {
                if (axleInfo.steering)
                {
                    axleInfo.leftWheel.steerAngle = steering;
                    axleInfo.rightWheel.steerAngle = steering;
                }
                if (axleInfo.motor)
                {
                    axleInfo.leftWheel.motorTorque = motor;
                    axleInfo.rightWheel.motorTorque = motor;
                }
                ApplyLocalPositionToVisuals(axleInfo.leftWheel);
                ApplyLocalPositionToVisuals(axleInfo.rightWheel);
            }
        }

        private void Update()
        {
            realtimeSinceStartup += Time.deltaTime;
            if (GetKeyDownSpace())
            {
                mainLights = !mainLights;
                lightsComponent.SetMainLights(mainLights);
            }

            if (GetKeyDownQ())
            {
                blinkLeft = !blinkLeft;
                if (blinkLeft == true)
                {
                    blinkRifgt = false;
                    lightsComponent.SetBlinker(BlinkType.Left);
                }
                else
                {
                    lightsComponent.SetBlinker(BlinkType.Stop);
                }
            }

            if (GetKeyDownE())
            {
                blinkRifgt = !blinkRifgt;
                if (blinkRifgt == true)
                {
                    blinkLeft = false;
                    lightsComponent.SetBlinker(BlinkType.Right);
                }
                else
                {
                    lightsComponent.SetBlinker(BlinkType.Stop);
                }
            }

            lightsComponent.SetBrakeLights(brake);
            lightsComponent.SetReverseLights(reverse);
            lightsComponent.UpdateLights(realtimeSinceStartup);
        }

        private bool GetKeyDownSpace()
        {
#if !ENABLE_LEGACY_INPUT_MANAGER
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }
        private bool GetKeyDownQ()
        {
#if !ENABLE_LEGACY_INPUT_MANAGER
            return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
    return Input.GetKeyDown(KeyCode.Q);
#endif
        }

        private bool GetKeyDownE()
        {
#if !ENABLE_LEGACY_INPUT_MANAGER
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
    return Input.GetKeyDown(KeyCode.E);
#endif
        }
    }
}