using UnityEngine;

public class AttackTarget : MonoBehaviour
{
    [SerializeField] private Transform targetPoint;
    [SerializeField] private bool targetable = true;
    public Transform TargetPoint => targetPoint != null ? targetPoint : transform;
    public bool Targetable => targetable && isActiveAndEnabled;
}
