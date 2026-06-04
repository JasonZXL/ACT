using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PlayerDefeatMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CombatHealth playerHealth;
    [SerializeField] private GameObject defeatPanel;
    [SerializeField] private Button firstSelectedButton;

    [Header("Display")]
    [SerializeField] private bool showFromAnimationEvent = true;
    [SerializeField] private float fallbackShowDelay = 2f;
    [SerializeField] private bool pauseWhenShown = true;
    [SerializeField] private bool unlockCursorWhenShown = true;
    [SerializeField] private bool disableCursorLockScriptsWhenShown = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private Coroutine _fallbackRoutine;
    private bool _shown;
    private float _previousTimeScale = 1f;
    private PlayModeCursorLock[] _disabledCursorLocks;

    private void Awake()
    {
        if (playerHealth == null)
        {
            playerHealth = FindObjectOfType<CombatHealth>();
        }

        if (defeatPanel != null)
        {
            defeatPanel.SetActive(false);
        }

        if (firstSelectedButton == null && defeatPanel != null)
        {
            firstSelectedButton = defeatPanel.GetComponentInChildren<Button>(true);
        }
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died += OnPlayerDied;
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -= OnPlayerDied;
        }

        if (_fallbackRoutine != null)
        {
            StopCoroutine(_fallbackRoutine);
            _fallbackRoutine = null;
        }
    }

    private void OnPlayerDied(CombatHealth deadHealth)
    {
        if (playerHealth != null && deadHealth != playerHealth)
        {
            return;
        }

        LogDebug("Player died.");

        if (!showFromAnimationEvent)
        {
            ShowDefeatMenu();
            return;
        }

        if (fallbackShowDelay <= 0f)
        {
            return;
        }

        if (_fallbackRoutine != null)
        {
            StopCoroutine(_fallbackRoutine);
        }

        _fallbackRoutine = StartCoroutine(ShowFallbackRoutine());
    }

    private IEnumerator ShowFallbackRoutine()
    {
        yield return new WaitForSecondsRealtime(fallbackShowDelay);
        ShowDefeatMenu();
    }

    public void AE_ShowDefeatMenu()
    {
        ShowDefeatMenu();
    }

    public void ShowDefeatMenu()
    {
        if (_shown)
        {
            return;
        }

        _shown = true;
        if (_fallbackRoutine != null)
        {
            StopCoroutine(_fallbackRoutine);
            _fallbackRoutine = null;
        }

        if (defeatPanel != null)
        {
            defeatPanel.SetActive(true);
        }

        if (disableCursorLockScriptsWhenShown)
        {
            DisableCursorLockScripts();
        }

        if (unlockCursorWhenShown)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        SelectDefaultButton();

        if (pauseWhenShown)
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        LogDebug("Defeat menu shown.");
    }

    public void RetryCurrentScene()
    {
        LogDebug("Retry button clicked. Reloading current scene.");
        Time.timeScale = 1f;
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
    }

    public void QuitGame()
    {
        LogDebug("Quit button clicked.");
        Time.timeScale = _previousTimeScale <= 0f ? 1f : _previousTimeScale;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void DisableCursorLockScripts()
    {
        PlayModeCursorLock[] cursorLocks = FindObjectsOfType<PlayModeCursorLock>(true);
        int disabledCount = 0;
        _disabledCursorLocks = cursorLocks;

        for (int i = 0; i < cursorLocks.Length; i++)
        {
            PlayModeCursorLock cursorLock = cursorLocks[i];
            if (cursorLock == null || !cursorLock.enabled)
            {
                continue;
            }

            cursorLock.enabled = false;
            disabledCount++;
        }

        LogDebug($"Cursor lock scripts disabled. count={disabledCount}");
    }

    private void SelectDefaultButton()
    {
        if (EventSystem.current == null || firstSelectedButton == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(firstSelectedButton.gameObject);
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[PlayerDefeatMenu] {message} time={Time.unscaledTime:F3}", this);
    }
}
