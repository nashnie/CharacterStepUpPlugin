using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct HitResult
{
    public bool bBlockingHit;
    public bool bStartPenetrating;
    public int faceIndex;
    public float time;
    public float distance;

    //???
    public Vector3 Location;

    public Vector3 ImpactPoint;

    public Vector3 Normal;

    public Vector3 ImpactNormal;

    public Vector3 TraceStart;

    public Vector3 TraceEnd;

    public float PenetrationDepth;

    //??
    public int Item;

    public Transform Actor;

    public CapsuleCollider Component;

    public RaycastHit raycastHit;
}
