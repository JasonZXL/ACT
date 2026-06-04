using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CinemachineCamera))]
public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float zoomLerpSpeed = 10f;
    [SerializeField] private float minDistance = 3f;
    [SerializeField] private float maxDistance = 15f;

    private CinemachineCamera _cinemachineCamera;
    private CinemachineOrbitalFollow _orbitalFollow;
    private float _targetZoom;
    private float _currentZoom;

    private void Awake()
    {
        _cinemachineCamera = GetComponent<CinemachineCamera>();
        _orbitalFollow = GetComponent<CinemachineOrbitalFollow>();

        if (_orbitalFollow != null)
        {
            _currentZoom = _orbitalFollow.Radius;
            _targetZoom = _currentZoom;
        }
    }

    private void Update()
    {
        if (_orbitalFollow == null)
        {
            return;
        }

        HandleMouseWheelZoom();

        _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, Time.deltaTime * zoomLerpSpeed);
        _orbitalFollow.Radius = _currentZoom;
    }

    private void HandleMouseWheelZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        float scrollY = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scrollY) < 0.01f)
        {
            return;
        }

        _targetZoom = Mathf.Clamp(
            _targetZoom - scrollY * 0.01f * zoomSpeed,
            minDistance,
            maxDistance);
    }
}
