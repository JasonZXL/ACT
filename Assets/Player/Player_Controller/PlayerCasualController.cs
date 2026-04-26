using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerCasualController : MonoBehaviour
{
    public enum CasualMoveMode
    {
        Idle = 0,
        Jog = 1,
        Boost = 2
    }

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    [Header("Casual Movement")]
    [SerializeField] private float jogSpeed = 4.5f;
    [SerializeField] private float boostSpeed = 9f;
    [SerializeField] private float minTurnSpeed = 360f;
    [SerializeField] private float maxTurnSpeed = 1080f;
    [SerializeField] private bool shouldFaceMoveDirection = true;
    [SerializeField] private float moveInputGraceTime = 0.08f;

    private static readonly int HashHasMoveInput = Animator.StringToHash("HasMoveInput");
    private static readonly int HashMoveMode = Animator.StringToHash("MoveMode");

    private CharacterController _controller;
    private Animator _animator;
    private Vector2 _moveInput;
    private Vector3 _moveDirection;
    private bool _hasMoveInput;
    private float _lastMoveInputTime = -999f;
    private CasualMoveMode _moveMode = CasualMoveMode.Idle;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        ResetState();
    }

    private void Update()
    {
        ResolveCameraReference();
        ReadMovementInput();
        UpdateMoveDirection();
        MoveCharacter();
        FaceMoveDirection();
        UpdateAnimatorParameters();
    }

    public void ResetState()
    {
        _moveInput = Vector2.zero;
        _moveDirection = Vector3.zero;
        _hasMoveInput = false;
        _moveMode = CasualMoveMode.Idle;
        _lastMoveInputTime = -999f;
    }

    public int GetMoveMode()
    {
        return (int)_moveMode;
    }

    private void ResolveCameraReference()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void ReadMovementInput()
    {
        if (!TryReadKeyboard(out Vector2 moveInput, out bool shiftPressed))
        {
            _moveInput = Vector2.zero;
            _hasMoveInput = false;
            _moveMode = CasualMoveMode.Idle;
            return;
        }

        _moveInput = moveInput;
        bool hasRawMoveInput = _moveInput.sqrMagnitude > 0.001f;
        if (hasRawMoveInput)
        {
            _lastMoveInputTime = Time.time;
        }

        _hasMoveInput = hasRawMoveInput || Time.time - _lastMoveInputTime <= moveInputGraceTime;
        _moveMode = !_hasMoveInput ? CasualMoveMode.Idle : shiftPressed ? CasualMoveMode.Boost : CasualMoveMode.Jog;
    }

    private bool TryReadKeyboard(out Vector2 moveInput, out bool shiftPressed)
    {
        shiftPressed = false;
        moveInput = Vector2.zero;

        if (UnityEngine.InputSystem.Keyboard.current == null)
        {
            return false;
        }

        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        float x = 0f;
        float y = 0f;

        if (keyboard.aKey.isPressed && !keyboard.dKey.isPressed)
        {
            x = -1f;
        }
        else if (keyboard.dKey.isPressed && !keyboard.aKey.isPressed)
        {
            x = 1f;
        }

        if (keyboard.wKey.isPressed && !keyboard.sKey.isPressed)
        {
            y = 1f;
        }
        else if (keyboard.sKey.isPressed && !keyboard.wKey.isPressed)
        {
            y = -1f;
        }

        moveInput = new Vector2(x, y);
        if (moveInput.sqrMagnitude > 1f)
        {
            moveInput.Normalize();
        }

        shiftPressed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        return true;
    }

    private void UpdateMoveDirection()
    {
        if (!_hasMoveInput)
        {
            _moveDirection = Vector3.zero;
            return;
        }

        if (cameraTransform == null)
        {
            _moveDirection = new Vector3(_moveInput.x, 0f, _moveInput.y).normalized;
            return;
        }

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 worldDirection = forward * _moveInput.y + right * _moveInput.x;
        _moveDirection = worldDirection.sqrMagnitude > 0.001f ? worldDirection.normalized : Vector3.zero;
    }

    private void MoveCharacter()
    {
        float speed = _moveMode switch
        {
            CasualMoveMode.Jog => jogSpeed,
            CasualMoveMode.Boost => boostSpeed,
            _ => 0f
        };

        _controller.Move(_moveDirection * speed * Time.deltaTime);
    }

    private void FaceMoveDirection()
    {
        if (!shouldFaceMoveDirection || _moveDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        float targetYaw = Mathf.Atan2(_moveDirection.x, _moveDirection.z) * Mathf.Rad2Deg;
        float currentYaw = transform.eulerAngles.y;
        float angleDelta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));
        float turnSpeed = Mathf.Lerp(minTurnSpeed, maxTurnSpeed, angleDelta / 180f);
        float nextYaw = Mathf.MoveTowardsAngle(
            currentYaw,
            targetYaw,
            turnSpeed * Time.deltaTime);

        transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
    }

    private void UpdateAnimatorParameters()
    {
        _animator.SetBool(HashHasMoveInput, _hasMoveInput);
        _animator.SetInteger(HashMoveMode, (int)_moveMode);
    }
}
