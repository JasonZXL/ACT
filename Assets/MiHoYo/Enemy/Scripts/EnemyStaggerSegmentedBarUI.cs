using UnityEngine;
using UnityEngine.UI;

public class EnemyStaggerSegmentedBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyStaggerController staggerController;
    [SerializeField] private RectTransform root;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("UI Images")]
    [SerializeField] private Image[] fillImages;

    [Header("World Billboard")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.45f, 0f);
    [SerializeField] private bool followTargetPosition = true;
    [SerializeField] private bool faceCamera = true;

    [Header("Display")]
    [SerializeField] private bool hideWhenFull = false;
    [SerializeField] private bool hideWhenStaggeredComplete = false;

    [Header("Options")]
    [SerializeField] private bool autoFindReferences = true;
    [SerializeField] private bool autoFindImages = true;
    [SerializeField] private string fillObjectName = "Fill";

    private void Awake()
    {
        if (root == null)
        {
            root = transform as RectTransform;
        }

        ResolveReferences();
        ResolveImages();
        ConfigureImages();
        UpdateBar();
    }

    private void OnValidate()
    {
        ConfigureImages();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        UpdateWorldTransform();
        UpdateBar();
    }

    private void ResolveReferences()
    {
        if (autoFindReferences && staggerController == null)
        {
            staggerController = GetComponentInParent<EnemyStaggerController>();
            if (staggerController == null)
            {
                staggerController = FindFirstObjectByType<EnemyStaggerController>();
            }
        }

        if (followTarget == null && staggerController != null)
        {
            followTarget = staggerController.transform;
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void ResolveImages()
    {
        if (!autoFindImages)
        {
            return;
        }

        if (fillImages == null || fillImages.Length == 0)
        {
            fillImages = FindSegmentFillImages(fillObjectName);
        }
    }

    private Image[] FindSegmentFillImages(string objectName)
    {
        int count = transform.childCount;
        Image[] images = new Image[count];
        int foundCount = 0;

        for (int i = 0; i < count; i++)
        {
            Transform segment = transform.GetChild(i);
            Transform target = segment.Find(objectName);
            if (target == null)
            {
                continue;
            }

            Image image = target.GetComponent<Image>();
            if (image == null)
            {
                continue;
            }

            images[foundCount] = image;
            foundCount++;
        }

        Image[] trimmed = new Image[foundCount];
        for (int i = 0; i < foundCount; i++)
        {
            trimmed[i] = images[i];
        }

        return trimmed;
    }

    private void ConfigureImages()
    {
        if (fillImages == null)
        {
            return;
        }

        for (int i = 0; i < fillImages.Length; i++)
        {
            Image fillImage = fillImages[i];
            if (fillImage == null)
            {
                continue;
            }

            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
        }
    }

    private void UpdateBar()
    {
        if (staggerController == null || fillImages == null || fillImages.Length == 0)
        {
            SetVisible(false);
            return;
        }

        float maxValue = Mathf.Max(1f, staggerController.RequiredStaggerValue);
        float displayValue = staggerController.IsStaggered
            ? staggerController.StaggerFillNormalized * maxValue
            : staggerController.RemainingStaggerValue;
        float segmentValue = maxValue / fillImages.Length;

        for (int i = 0; i < fillImages.Length; i++)
        {
            if (fillImages[i] == null)
            {
                continue;
            }

            fillImages[i].fillAmount = GetSegmentFillAmount(displayValue, i, segmentValue);
        }

        bool visible = true;
        if (hideWhenFull && !staggerController.IsStaggered && staggerController.RemainingStaggerValue >= staggerController.RequiredStaggerValue)
        {
            visible = false;
        }
        else if (hideWhenStaggeredComplete && staggerController.IsStaggered && staggerController.StaggerFillNormalized >= 0.999f)
        {
            visible = false;
        }

        SetVisible(visible);
    }

    private void UpdateWorldTransform()
    {
        if (root == null)
        {
            return;
        }

        if (followTargetPosition && followTarget != null)
        {
            root.position = followTarget.TransformPoint(localOffset);
        }

        if (!faceCamera || targetCamera == null)
        {
            return;
        }

        Vector3 cameraDirection = root.position - targetCamera.transform.position;
        if (cameraDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        root.rotation = Quaternion.LookRotation(cameraDirection.normalized, Vector3.up);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            return;
        }

        if (fillImages == null)
        {
            return;
        }

        for (int i = 0; i < fillImages.Length; i++)
        {
            if (fillImages[i] != null)
            {
                fillImages[i].enabled = visible;
            }
        }
    }

    private float GetSegmentFillAmount(float value, int segmentIndex, float segmentValue)
    {
        float segmentStart = segmentIndex * segmentValue;
        float filledValue = Mathf.Clamp(value - segmentStart, 0f, segmentValue);
        return segmentValue <= 0f ? 0f : filledValue / segmentValue;
    }
}
