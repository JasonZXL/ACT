using UnityEngine;
using UnityEngine.InputSystem;

public class PlayModeCursorLock : MonoBehaviour
{
    [SerializeField] private bool lockOnEnable = true;
    [SerializeField] private bool hideCursor = true;
    [SerializeField] private bool allowEscapeToUnlock = true;
    [SerializeField] private bool relockOnMouseClick = true;

    private void OnEnable()
    {
        if (lockOnEnable)
        {
            LockCursor();
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (allowEscapeToUnlock && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            UnlockCursor();
            return;
        }

        if (relockOnMouseClick &&
            mouse != null &&
            mouse.leftButton.wasPressedThisFrame &&
            Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor();
        }
    }

    private void OnDisable()
    {
        UnlockCursor();
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = !hideCursor;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
