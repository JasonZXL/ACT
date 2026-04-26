using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerCombatController : MonoBehaviour
{
    public enum CombatMoveState
    {
        Idle = 0,
        Jog = 1,
        Boost = 2
    }

    public enum CombatMoveMode
    {
        Idle = 0,
        Move = 1
    }

    public enum DodgeDirection
    {
        Forward = 0,
        Backward = 1,
        Left = 2,
        Right = 3
    }

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    [Header("Combat Movement")]
    [SerializeField] private float combatMoveSpeed = 4f;
    [SerializeField] private float combatBoostSpeed = 7f;
    [SerializeField] private float combatAttackMoveMultiplier = 0.35f;
    [SerializeField] private float dodgeSpeed = 7f;
    [SerializeField] private float dodgeDuration = 0.28f;
    [SerializeField] private float parryLockDuration = 0.25f;
    [SerializeField] private float minTurnSpeed = 360f;
    [SerializeField] private float maxTurnSpeed = 1080f;
    [SerializeField] private bool shouldFaceMoveDirection = true;
    [SerializeField] private float moveInputGraceTime = 0.08f;

    [Header("Focus")]
    [SerializeField] private float maxFocus = 300f;
    [SerializeField] private float focusPerSegment = 100f;

    [Header("Heavy Charge")]
    [SerializeField] private float heavyChargeRate = 240f;
    [SerializeField] private float heavyChargeDeadZone = 30f;

    private static readonly int HashHasMoveInput = Animator.StringToHash("HasMoveInput");
    private static readonly int HashMoveMode = Animator.StringToHash("MoveMode");
    private static readonly int HashCombatMoveMode = Animator.StringToHash("CombatMoveMode");
    private static readonly int HashCombatMoveMagnitude = Animator.StringToHash("CombatMoveMagnitude");
    private static readonly int HashFocusValue = Animator.StringToHash("FocusValue");
    private static readonly int HashIsChargingHeavy = Animator.StringToHash("IsChargingHeavy");
    private static readonly int HashHeavyAttackLevel = Animator.StringToHash("HeavyAttackLevel");
    private static readonly int HashLightAttackIndex = Animator.StringToHash("LightAttackIndex");
    private static readonly int HashDodgeDirection = Animator.StringToHash("DodgeDirection");
    private static readonly int HashLightAttackTrigger = Animator.StringToHash("LightAttack");
    private static readonly int HashHeavyAttackTrigger = Animator.StringToHash("HeavyAttack");
    private static readonly int HashParryTrigger = Animator.StringToHash("Parry");
    private static readonly int HashDodgeTrigger = Animator.StringToHash("Dodge");

    private CharacterController _controller;
    private Animator _animator;
    private Vector2 _moveInput;
    private Vector3 _moveDirection;
    private Vector3 _dodgeDirection;
    private bool _hasMoveInput;
    private float _lastMoveInputTime = -999f;
    private CombatMoveState _moveState = CombatMoveState.Idle;
    private CombatMoveMode _moveMode = CombatMoveMode.Idle;

    private float _currentFocus;
    private bool _leftMouseHeld;
    private float _heavyChargePoints;
    private bool _isChargingHeavy;
    private bool _queuedLightAttack;
    private bool _comboWindowOpen;
    private int _currentLightAttackIndex;
    private bool _isAttackLocked;
    private bool _isDodging;
    private float _dodgeTimer;
    private float _parryLockTimer;

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
        ReadCombatInput();
        UpdateMoveDirection();
        UpdateActionTimers();
        MoveCharacter();
        FaceMoveDirection();
        UpdateAnimatorParameters();
    }

    public void ResetState()
    {
        _moveInput = Vector2.zero;
        _moveDirection = Vector3.zero;
        _dodgeDirection = Vector3.zero;
        _hasMoveInput = false;
        _lastMoveInputTime = -999f;
        _moveState = CombatMoveState.Idle;
        _moveMode = CombatMoveMode.Idle;
        _leftMouseHeld = false;
        _heavyChargePoints = 0f;
        _isChargingHeavy = false;
        _queuedLightAttack = false;
        _comboWindowOpen = false;
        _currentLightAttackIndex = 0;
        _isAttackLocked = false;
        _isDodging = false;
        _dodgeTimer = 0f;
        _parryLockTimer = 0f;
    }

    public float GetCurrentFocus()
    {
        return _currentFocus;
    }

    public void GainFocus(float amount)
    {
        _currentFocus = Mathf.Clamp(_currentFocus + amount, 0f, maxFocus);
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
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            _moveInput = Vector2.zero;
            _hasMoveInput = false;
            _moveState = CombatMoveState.Idle;
            _moveMode = CombatMoveMode.Idle;
            return;
        }

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

        _moveInput = new Vector2(x, y);
        if (_moveInput.sqrMagnitude > 1f)
        {
            _moveInput.Normalize();
        }

        bool hasRawMoveInput = _moveInput.sqrMagnitude > 0.001f;
        if (hasRawMoveInput)
        {
            _lastMoveInputTime = Time.time;
        }

        bool shiftPressed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        _hasMoveInput = hasRawMoveInput || Time.time - _lastMoveInputTime <= moveInputGraceTime;
        _moveState = !_hasMoveInput ? CombatMoveState.Idle : shiftPressed ? CombatMoveState.Boost : CombatMoveState.Jog;
        _moveMode = _hasMoveInput ? CombatMoveMode.Move : CombatMoveMode.Idle;
    }

    private void ReadCombatInput()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null)
        {
            return;
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            TryStartCombatDodge();
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            TryStartParry();
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            _leftMouseHeld = true;
            _heavyChargePoints = 0f;
        }

        if (_leftMouseHeld && !_isAttackLocked && !_isDodging && _parryLockTimer <= 0f)
        {
            _heavyChargePoints = Mathf.Clamp(_heavyChargePoints + heavyChargeRate * Time.deltaTime, 0f, maxFocus);
            _isChargingHeavy = _heavyChargePoints > heavyChargeDeadZone;
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            ResolveLeftMouseRelease();
        }
    }

    private void ResolveLeftMouseRelease()
    {
        if (!_leftMouseHeld)
        {
            return;
        }

        _leftMouseHeld = false;

        if (_isChargingHeavy && !_isAttackLocked && !_isDodging && _parryLockTimer <= 0f)
        {
            if (!TryStartHeavyAttack())
            {
                TryStartLightAttack();
            }
        }
        else
        {
            if (_isAttackLocked)
            {
                _queuedLightAttack = true;
            }
            else
            {
                TryStartLightAttack();
            }
        }

        _heavyChargePoints = 0f;
        _isChargingHeavy = false;
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

    private void UpdateActionTimers()
    {
        if (_isDodging)
        {
            _dodgeTimer -= Time.deltaTime;
            if (_dodgeTimer <= 0f)
            {
                _isDodging = false;
            }
        }

        if (_parryLockTimer > 0f)
        {
            _parryLockTimer -= Time.deltaTime;
        }
    }

    private void MoveCharacter()
    {
        Vector3 horizontalVelocity;
        if (_isDodging)
        {
            horizontalVelocity = _dodgeDirection * dodgeSpeed;
        }
        else
        {
            float speed = _moveState switch
            {
                CombatMoveState.Jog => combatMoveSpeed,
                CombatMoveState.Boost => combatBoostSpeed,
                _ => 0f
            };

            if (_isAttackLocked)
            {
                speed *= combatAttackMoveMultiplier;
            }

            horizontalVelocity = _moveDirection * speed;
        }

        _controller.Move(horizontalVelocity * Time.deltaTime);
    }

    private void FaceMoveDirection()
    {
        Vector3 facingDirection = _isDodging ? _dodgeDirection : _moveDirection;
        if (!shouldFaceMoveDirection || facingDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        float targetYaw = Mathf.Atan2(facingDirection.x, facingDirection.z) * Mathf.Rad2Deg;
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
        _animator.SetInteger(HashMoveMode, (int)_moveState);
        _animator.SetInteger(HashCombatMoveMode, (int)_moveMode);
        _animator.SetFloat(HashCombatMoveMagnitude, _hasMoveInput ? _moveInput.magnitude : 0f);
        _animator.SetFloat(HashFocusValue, _currentFocus);
        _animator.SetBool(HashIsChargingHeavy, _isChargingHeavy);
    }

    private void TryStartCombatDodge()
    {
        if (_isAttackLocked || _isDodging || _parryLockTimer > 0f)
        {
            return;
        }

        _isDodging = true;
        _dodgeTimer = dodgeDuration;
        _dodgeDirection = ResolveDodgeDirection();
        _animator.SetInteger(HashDodgeDirection, (int)ResolveDodgeDirectionType());
        _animator.SetTrigger(HashDodgeTrigger);
    }

    private Vector3 ResolveDodgeDirection()
    {
        if (_moveDirection.sqrMagnitude > 0.001f)
        {
            return _moveDirection;
        }

        return transform.forward;
    }

    private DodgeDirection ResolveDodgeDirectionType()
    {
        if (_moveInput.y > 0.5f)
        {
            return DodgeDirection.Forward;
        }

        if (_moveInput.y < -0.5f)
        {
            return DodgeDirection.Backward;
        }

        if (_moveInput.x < -0.5f)
        {
            return DodgeDirection.Left;
        }

        if (_moveInput.x > 0.5f)
        {
            return DodgeDirection.Right;
        }

        return DodgeDirection.Forward;
    }

    private void TryStartParry()
    {
        if (_isAttackLocked || _isDodging || _parryLockTimer > 0f)
        {
            return;
        }

        _parryLockTimer = parryLockDuration;
        _animator.SetTrigger(HashParryTrigger);
    }

    private bool TryStartLightAttack()
    {
        if (_isDodging || _parryLockTimer > 0f)
        {
            return false;
        }

        if (_isAttackLocked)
        {
            _queuedLightAttack = true;
            return false;
        }

        _currentLightAttackIndex = Mathf.Clamp(_currentLightAttackIndex + 1, 1, 4);
        _isAttackLocked = true;
        _queuedLightAttack = false;
        _comboWindowOpen = false;
        _animator.SetInteger(HashLightAttackIndex, _currentLightAttackIndex);
        _animator.SetTrigger(HashLightAttackTrigger);
        return true;
    }

    private bool TryStartHeavyAttack()
    {
        if (_isAttackLocked || _isDodging || _parryLockTimer > 0f)
        {
            return false;
        }

        int desiredTier = ResolveDesiredHeavyTier(_heavyChargePoints);
        if (desiredTier <= 0)
        {
            return false;
        }

        int availableTier = Mathf.FloorToInt(_currentFocus / focusPerSegment);
        int actualTier = Mathf.Min(desiredTier, availableTier);
        if (actualTier <= 0)
        {
            return false;
        }

        SpendFocus(actualTier * focusPerSegment);
        _isAttackLocked = true;
        _queuedLightAttack = false;
        _comboWindowOpen = false;
        _currentLightAttackIndex = 0;
        _animator.SetInteger(HashHeavyAttackLevel, actualTier);
        _animator.SetTrigger(HashHeavyAttackTrigger);
        return true;
    }

    private int ResolveDesiredHeavyTier(float chargePoints)
    {
        if (chargePoints <= heavyChargeDeadZone)
        {
            return 0;
        }

        if (chargePoints < 100f)
        {
            return 1;
        }

        if (chargePoints < 200f)
        {
            return 2;
        }

        return 3;
    }

    private void SpendFocus(float amount)
    {
        _currentFocus = Mathf.Max(0f, _currentFocus - amount);
    }

    public void AE_OpenComboWindow()
    {
        _comboWindowOpen = true;
    }

    public void AE_CloseComboWindow()
    {
        _comboWindowOpen = false;
    }

    public void AE_EndAttackLock()
    {
        _isAttackLocked = false;

        if (_queuedLightAttack && _comboWindowOpen)
        {
            _queuedLightAttack = false;
            TryStartLightAttack();
            return;
        }

        if (!_queuedLightAttack)
        {
            _currentLightAttackIndex = 0;
        }
    }

    public void AE_ForceResetAttackState()
    {
        _queuedLightAttack = false;
        _comboWindowOpen = false;
        _currentLightAttackIndex = 0;
        _isAttackLocked = false;
        _isChargingHeavy = false;
        _heavyChargePoints = 0f;
    }
}
