using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct FindFloorResult
{
    public bool bBlockingHit;
    public bool bWalkableFloor;
    public bool bLineTrace;
    public float floorDist;
    public float lineDist;

    public HitResult hitResult;
}
