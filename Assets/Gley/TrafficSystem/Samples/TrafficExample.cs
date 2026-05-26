using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif
using UnityEngine.SceneManagement;

namespace Gley.TrafficSystem.Internal
{
    public class TrafficExample : MonoBehaviour
    {
        [SerializeField] private Transform _busStops;
        private bool _pathSet;
        private int _stopNumber;
        private bool _followVehicle;
        private Transform _player;

        private const int _vehicleToFollow = 23;

        private void Start()
        {
            _player = GameObject.Find("Player").transform;
        }

        //every time a destination is reached, a new one is selected
        private void BusStationReached(int vehicleIndex)
        {
            //remove listener otherwise this method will be called on each frame
            Events.OnDestinationReached -= BusStationReached;
            if (vehicleIndex == 0)
            {
                _stopNumber++;
                if (_stopNumber == _busStops.childCount)
                {
                    _stopNumber = 0;
                }
                //stop and wait for 5 seconds, then move to the next destination
                Invoke("ContinueDriving", 5);
            }
        }

        /// <summary>
        /// Continue on path
        /// </summary>
        private void ContinueDriving()
        {
            Events.OnDestinationReached += BusStationReached;
            API.SetDestination(0, _busStops.GetChild(_stopNumber).transform.position);
        }

        private void Update()
        {
            if (!_pathSet)
            {
                if (API.IsInitialized())
                {
                    _pathSet = true;
                    SetPath();
                }
            }

            if (GetKeyDownF())
            {
                _followVehicle = !_followVehicle;
                if (_followVehicle)
                {
                    GameObject.Find("Main Camera").GetComponent<UrbanSystem.CameraFollow>().target = API.GetVehicleComponent(_vehicleToFollow).transform;
                    API.SetCamera(API.GetVehicleComponent(_vehicleToFollow).transform);
                }
                else
                {
                    GameObject.Find("Main Camera").GetComponent<UrbanSystem.CameraFollow>().target = _player;
                    API.SetCamera(_player);
                }
            }

            if (GetKeyDownESC())
            {
                Application.Quit();
            }

            if (GetKeyDownR())
            {
                SceneManager.LoadScene(0);
            }
        }

        /// <summary>
        /// set a path towards destination
        /// </summary>
        private void SetPath()
        {
            VehicleComponent vehicleComponent = API.GetVehicleComponent(0);
            if (vehicleComponent.gameObject.activeSelf)
            {
                Events.OnDestinationReached += BusStationReached;
                API.SetDestination(0, _busStops.GetChild(_stopNumber).transform.position);
            }
            else
            {
                Invoke("SetPath", 1);
            }
        }

        private bool GetKeyDownF()
        {
#if !ENABLE_LEGACY_INPUT_MANAGER
            return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F);
#endif
        }

        private bool GetKeyDownR()
        {
#if !ENABLE_LEGACY_INPUT_MANAGER
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        private bool GetKeyDownESC()
        {
#if !ENABLE_LEGACY_INPUT_MANAGER
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }


        //remove listeners
        private void OnDestroy()
        {
            Events.OnDestinationReached -= BusStationReached;
        }
    }
}