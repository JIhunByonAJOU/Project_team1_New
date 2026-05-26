#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine;
using UnityEngine.InputSystem;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using UnityEngine.SceneManagement;
#endif

namespace Gley.UrbanSystem
{
    public class UIInputNew : MonoBehaviour, IUIInput
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        // Events used for UI buttons only on mobile devices
        public delegate void ButtonDown(string button);
        public static event ButtonDown onButtonDown;

        public static void TriggerButtonDownEvent(string button)
        {
            onButtonDown?.Invoke(button);
        }

        public delegate void ButtonUp(string button);
        public static event ButtonUp onButtonUp;

        public static void TriggerButtonUpEvent(string button)
        {
            onButtonUp?.Invoke(button);
        }

        private bool left, right, up, down;
#endif

        private float horizontalInput;
        private float verticalInput;

#if !(UNITY_ANDROID || UNITY_IOS) || UNITY_EDITOR
        private InputAction _moveAction;
#endif

        /// <summary>
        /// Initializes the input system based on platform used
        /// </summary>
        public UIInputNew Initialize()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            onButtonDown += PointerDown;
            onButtonUp += PointerUp;
#else
            GameObject steeringUI = GameObject.Find("SteeringUI");
            if (steeringUI)
            {
                steeringUI.SetActive(false);
            }

            SetupDesktopInput();
#endif
            return this;
        }

#if !(UNITY_ANDROID || UNITY_IOS) || UNITY_EDITOR
        private void SetupDesktopInput()
        {
            _moveAction = new InputAction("Move", InputActionType.Value);

            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            _moveAction.AddBinding("<Gamepad>/leftStick");

            _moveAction.Enable();
        }
#endif

        /// <summary>
        /// Get steering input
        /// </summary>
        public float GetHorizontalInput()
        {
            return horizontalInput;
        }

        /// <summary>
        /// Get acceleration input
        /// </summary>
        public float GetVerticalInput()
        {
            return verticalInput;
        }

        /// <summary>
        /// Read input
        /// </summary>
        private void Update()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            // Horizontal
            if (left)
            {
                horizontalInput -= Time.deltaTime;
            }
            else if (right)
            {
                horizontalInput += Time.deltaTime;
            }
            else
            {
                horizontalInput = Mathf.MoveTowards(horizontalInput, 0, 5 * Time.deltaTime);
            }

            horizontalInput = Mathf.Clamp(horizontalInput, -1f, 1f);

            // Vertical
            if (up)
            {
                verticalInput += Time.deltaTime;
            }
            else if (down)
            {
                verticalInput -= Time.deltaTime;
            }
            else
            {
                verticalInput = 0;
            }

            verticalInput = Mathf.Clamp(verticalInput, -1f, 1f);
#else
            Vector2 input = _moveAction.ReadValue<Vector2>();
            horizontalInput = input.x;
            verticalInput = input.y;
#endif
        }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        private void PointerDown(string name)
        {
            if (name == "Restart")
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }

            if (name == "Left")
            {
                left = true;
                right = false;
            }
            if (name == "Right")
            {
                right = true;
                left = false;
            }
            if (name == "Up")
            {
                up = true;
                down = false;
            }
            if (name == "Down")
            {
                down = true;
                up = false;
            }
        }

        private void PointerUp(string name)
        {
            if (name == "Left") left = false;
            if (name == "Right") right = false;
            if (name == "Up") up = false;
            if (name == "Down") down = false;
        }
#endif

        private void OnDestroy()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            onButtonDown -= PointerDown;
            onButtonUp -= PointerUp;
#else
            _moveAction?.Disable();
#endif
        }
    }
}
#endif