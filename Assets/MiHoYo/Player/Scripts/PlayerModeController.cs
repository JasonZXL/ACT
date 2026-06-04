using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
public class PlayerModeController : MonoBehaviour
{
    [SerializeField] private PlayerCasualController casualController;
    [SerializeField] private KianaCombatController combatController;
    [SerializeField] private bool startInCombatMode;

    private static readonly int HashCombatMode = Animator.StringToHash("CombatMode");
    private static readonly int HashEnterCombatTrigger = Animator.StringToHash("EnterCombat");
    private static readonly int HashExitCombatTrigger = Animator.StringToHash("ExitCombat");

    private Animator _animator;
    private bool _isInCombatMode;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        if (casualController == null)
        {
            casualController = GetComponent<PlayerCasualController>();
        }

        if (combatController == null)
        {
            combatController = GetComponent<KianaCombatController>();
        }
    }

    private void Start()
    {
        ApplyMode(startInCombatMode, false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.rKey.wasPressedThisFrame)
        {
            ApplyMode(!_isInCombatMode, true);
        }
    }

    public bool IsInCombatMode()
    {
        return _isInCombatMode;
    }

    public void SetCombatMode(bool isInCombatMode)
    {
        ApplyMode(isInCombatMode, true);
    }

    private void ApplyMode(bool isInCombatMode, bool sendAnimatorTransition)
    {
        _isInCombatMode = isInCombatMode;

        if (casualController != null)
        {
            casualController.enabled = !_isInCombatMode;
            if (!_isInCombatMode)
            {
                casualController.ResetState();
            }
        }

        if (combatController != null)
        {
            combatController.enabled = _isInCombatMode;
            if (_isInCombatMode)
            {
                combatController.ResetState();
            }
        }

        _animator.SetBool(HashCombatMode, _isInCombatMode);

        if (!sendAnimatorTransition)
        {
            return;
        }

        if (_isInCombatMode)
        {
            _animator.SetTrigger(HashEnterCombatTrigger);
        }
        else
        {
            _animator.SetTrigger(HashExitCombatTrigger);
        }
    }
}
