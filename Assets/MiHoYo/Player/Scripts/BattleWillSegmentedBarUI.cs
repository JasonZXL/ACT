using UnityEngine;
using UnityEngine.UI;

public class BattleWillSegmentedBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerBattleWillSystem battleWillSystem;
    [SerializeField] private KianaCombatController kianaCombatController;

    [Header("UI Images")]
    [SerializeField] private Image[] fillImages;

    [Header("Options")]
    [SerializeField] private bool autoFindReferences = true;
    [SerializeField] private bool autoFindImages = true;
    [SerializeField] private string fillObjectName = "Fill";

    private void Awake()
    {
        ResolveReferences();
        ResolveImages();
        ConfigureImages();
    }

    private void OnValidate()
    {
        ConfigureImages();
    }

    private void Update()
    {
        ResolveReferences();
        UpdateBar();
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (battleWillSystem == null)
        {
            battleWillSystem = FindFirstObjectByType<PlayerBattleWillSystem>();
        }

        if (kianaCombatController == null)
        {
            kianaCombatController = FindFirstObjectByType<KianaCombatController>();
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

        if (foundCount == images.Length)
        {
            return images;
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
        if (battleWillSystem == null || fillImages == null || fillImages.Length == 0)
        {
            return;
        }

        float maxValue = Mathf.Max(1f, battleWillSystem.MaxBattleWill);
        float currentValue = Mathf.Clamp(battleWillSystem.CurrentBattleWill, 0f, maxValue);
        float previewCost = kianaCombatController != null
            ? kianaCombatController.GetPreviewBattleWillCost()
            : 0f;
        float displayValue = Mathf.Clamp(currentValue - previewCost, 0f, maxValue);
        float segmentValue = maxValue / fillImages.Length;

        for (int i = 0; i < fillImages.Length; i++)
        {
            if (fillImages[i] == null)
            {
                continue;
            }

            fillImages[i].fillAmount = GetSegmentFillAmount(displayValue, i, segmentValue);
        }
    }

    private float GetSegmentFillAmount(float value, int segmentIndex, float segmentValue)
    {
        float segmentStart = segmentIndex * segmentValue;
        float filledValue = Mathf.Clamp(value - segmentStart, 0f, segmentValue);
        return segmentValue <= 0f ? 0f : filledValue / segmentValue;
    }
}
