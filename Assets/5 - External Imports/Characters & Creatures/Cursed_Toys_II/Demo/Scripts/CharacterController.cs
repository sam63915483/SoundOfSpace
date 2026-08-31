using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

namespace CursedToysII
{
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float walkSpeed = 3f;
        public float runSpeed = 6f;
        public float rotationSpeed = 10f;
        
        [Header("Camera Settings")]
        public Transform cameraTransform;
        public float mouseSensitivity = 2f;
        public float minVerticalAngle = -30f;
        public float maxVerticalAngle = 60f;
        public float cameraDistance = 5f;
        public float minCameraDistance = 2f;
        public float maxCameraDistance = 10f;
        public float zoomSpeed = 2f;
        public float cameraHeight = 2f;
        
        [Header("Death Settings")]
        public float deathWaitTime = 5f;
        
        // Input Actions
        private PlayerInput playerInput;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction runAction;
        private InputAction attackAction;
        private InputAction damageAction;
        private InputAction shiftAction;
        private InputAction scrollAction;
        
        // Private variables
        private CharacterController characterController;
        private Animator animator;
        private float verticalRotation = 0f;
        private float horizontalRotation = 0f;
        private bool isDead = false;
        private Vector3 cameraOffset;
        private float currentCameraDistance;
        
        void Awake()
        {
            // Get PlayerInput component or create input actions manually
            playerInput = GetComponent<PlayerInput>();
            
            // Create input actions manually if no PlayerInput component
            if (playerInput == null)
            {
                SetupInputActions();
            }
        }
        
        void SetupInputActions()
        {
            moveAction = new InputAction("Move", InputActionType.Value, "<Keyboard>/wasd");
            lookAction = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            runAction = new InputAction("Run", InputActionType.Button, "<Keyboard>/shift");
            attackAction = new InputAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            damageAction = new InputAction("Damage", InputActionType.Button, "<Keyboard>/f");
            shiftAction = new InputAction("Shift", InputActionType.Button, "<Keyboard>/shift");
            scrollAction = new InputAction("Scroll", InputActionType.Value, "<Mouse>/scroll/y");
            
            // Enable all actions
            moveAction.Enable();
            lookAction.Enable();
            runAction.Enable();
            attackAction.Enable();
            damageAction.Enable();
            shiftAction.Enable();
            scrollAction.Enable();
        }
        
        void Start()
        {
            // Get components
            characterController = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();
            
            // Setup camera if not assigned
            if (cameraTransform == null)
                cameraTransform = Camera.main.transform;
            
            // Lock cursor for better camera control
            Cursor.lockState = CursorLockMode.Locked;
            
            // Initialize current camera distance
            currentCameraDistance = cameraDistance;
            
            // Calculate initial camera offset
            UpdateCameraOffset();
        }
        
        void UpdateCameraOffset()
        {
            cameraOffset = new Vector3(0, cameraHeight, -currentCameraDistance);
        }
        
        void OnEnable()
        {
            if (playerInput == null)
            {
                moveAction?.Enable();
                lookAction?.Enable();
                runAction?.Enable();
                attackAction?.Enable();
                damageAction?.Enable();
                shiftAction?.Enable();
                scrollAction?.Enable();
            }
        }
        
        void OnDisable()
        {
            if (playerInput == null)
            {
                moveAction?.Disable();
                lookAction?.Disable();
                runAction?.Disable();
                attackAction?.Disable();
                damageAction?.Disable();
                shiftAction?.Disable();
                scrollAction?.Disable();
            }
        }
        
        void Update()
        {
            // Camera controls should always work
            HandleCameraRotation();
            HandleCameraZoom();
            UpdateCameraPosition();
            
            // Only handle character input and movement when not dead
            if (!isDead)
            {
                HandleInput();
                HandleMovement();
            }
            
            // Handle escape key for cursor toggle (using legacy input for this one key)
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                    Cursor.lockState = CursorLockMode.None;
                else
                    Cursor.lockState = CursorLockMode.Locked;
            }
        }
        
        void HandleInput()
        {
            // Attack input
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                animator.SetTrigger("Attack");
            }
            
            // Take damage input
            if (Keyboard.current.fKey.wasPressedThisFrame)
            {
                if (Keyboard.current.shiftKey.isPressed)
                {
                    // Die
                    StartCoroutine(HandleDeath());
                }
                else
                {
                    // Take damage
                    animator.SetTrigger("TakeDamage");
                }
            }
        }
        
        void HandleCameraRotation()
        {
            // Get mouse input
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            float mouseX = mouseDelta.x * mouseSensitivity * Time.deltaTime;
            float mouseY = mouseDelta.y * mouseSensitivity * Time.deltaTime;
            
            // Rotate camera horizontally around the character (free look)
            horizontalRotation += mouseX;
            
            // Rotate camera vertically
            verticalRotation -= mouseY;
            verticalRotation = Mathf.Clamp(verticalRotation, minVerticalAngle, maxVerticalAngle);
        }
        
        void HandleCameraZoom()
        {
            // Get scroll wheel input
            float scrollInput = Mouse.current.scroll.ReadValue().y;
            
            if (scrollInput != 0f)
            {
                // Adjust camera distance based on scroll input
                currentCameraDistance -= scrollInput * zoomSpeed * 0.1f; // Scale down scroll sensitivity
                currentCameraDistance = Mathf.Clamp(currentCameraDistance, minCameraDistance, maxCameraDistance);
                
                // Update the camera offset with new distance
                UpdateCameraOffset();
            }
        }
        
        void HandleMovement()
        {
            // Get input
            Vector2 moveInput = Vector2.zero;
            bool isRunning = false;
            
            // Read movement input
            if (Keyboard.current.wKey.isPressed) moveInput.y += 1f;
            if (Keyboard.current.sKey.isPressed) moveInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed) moveInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed) moveInput.x += 1f;
            
            // Check for running
            isRunning = Keyboard.current.shiftKey.isPressed;
            
            // Calculate movement direction relative to camera's horizontal rotation
            Vector3 direction = new Vector3(moveInput.x, 0, moveInput.y).normalized;
            
            if (direction.magnitude > 0.1f)
            {
                // Convert movement to world space based on camera direction
                Quaternion cameraRotation = Quaternion.Euler(0, horizontalRotation, 0);
                Vector3 moveDirection = cameraRotation * direction;
                
                // Rotate character to face movement direction
                if (moveDirection != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                }
                
                // Move the character
                float currentSpeed = isRunning ? runSpeed : walkSpeed;
                Vector3 velocity = moveDirection * currentSpeed;
                
                // Apply gravity
                if (!characterController.isGrounded)
                    velocity.y -= 9.81f;
                
                characterController.Move(velocity * Time.deltaTime);
                
                // Update animator parameters
                animator.SetFloat("Speed", direction.magnitude);
                animator.SetBool("IsRunning", isRunning);
            }
            else
            {
                // Character is idle
                animator.SetFloat("Speed", 0f);
                animator.SetBool("IsRunning", false);
            }
        }
        
        void UpdateCameraPosition()
        {
            // Calculate desired camera position using the camera's rotation, not character's rotation
            Quaternion horizontalRotationQ = Quaternion.Euler(0, horizontalRotation, 0);
            Quaternion verticalRotationQ = Quaternion.Euler(this.verticalRotation, 0, 0);
            
            Vector3 desiredPosition = transform.position + horizontalRotationQ * verticalRotationQ * cameraOffset;
            
            // Apply position to camera
            cameraTransform.position = desiredPosition;
            
            // Make camera look at character
            Vector3 lookTarget = transform.position + Vector3.up * (cameraHeight * 0.5f);
            cameraTransform.LookAt(lookTarget);
        }
        
        IEnumerator HandleDeath()
        {
            // Set death state
            isDead = true;
            animator.SetTrigger("Die");
            animator.SetBool("IsDead", true);
            
            // Keep the character in death state for the specified duration
            float deathTimer = 0f;
            while (deathTimer < deathWaitTime)
            {
                // Ensure the animator stays in death state
                animator.SetBool("IsDead", true);
                animator.SetFloat("Speed", 0f);
                animator.SetBool("IsRunning", false);
                
                deathTimer += Time.deltaTime;
                yield return null; // Wait one frame
            }
            
            // Now reset to idle after the wait time
            isDead = false;
            animator.SetBool("IsDead", false);
            
            // Force the animator to return to idle state
            animator.SetFloat("Speed", 0f);
            animator.SetBool("IsRunning", false);
            
            // Optional: Reset any other animator triggers that might be active
            animator.ResetTrigger("Die");
            animator.ResetTrigger("Attack");
            animator.ResetTrigger("TakeDamage");
        }
        
        void OnDestroy()
        {
            // Clean up input actions
            if (playerInput == null)
            {
                moveAction?.Dispose();
                lookAction?.Dispose();
                runAction?.Dispose();
                attackAction?.Dispose();
                damageAction?.Dispose();
                shiftAction?.Dispose();
                scrollAction?.Dispose();
            }
        }
    }
}