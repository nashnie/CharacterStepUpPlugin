using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterStepUpComponent : MonoBehaviour
{
    public float WalkableFloorAngle;
    public float WalkableFloorY;
    public float maxStepHeight;

    private CapsuleCollider capsuleCollider;
    private FindFloorResult currentFloor;
    private bool bConstrainToPlane;
    private Vector3 planeConstraintNormal;

    float pawnRadius, pawnHalfHeight;

    private const float SWEEP_EDGE_REJECT_DIST = 0.15f;
    private const float KINDA_SMALL_NUMBER = 1e-4f;
    private const float QUAT_TOLERANCE = 1e-8f;
    private const float BIG_NUMBER = 3.4e+38f;
    private const float MIN_FLOOR_DIST = 1.9f;
    private const float MAX_FLOOR_DIST = 2.4f;
    private const float DELTA = 0.00001f;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    bool StepUp(Vector3 gravDir, Vector3 delta, HitResult inHitResult, StepDownResult outStepDownResult)
    {
        if (CanStepUp(inHitResult) == false)
        {
            return false;
        }
        if (maxStepHeight <= 0f)
        {
            return false;
        }
        Vector3 oldLocation = transform.position;
       
        //TODO scale
        pawnRadius = capsuleCollider.radius;
        pawnHalfHeight = capsuleCollider.height / 2;

        float initialImpactY = inHitResult.ImpactPoint.y;
        if (initialImpactY > oldLocation.y + (pawnHalfHeight - pawnRadius))
        {
            return false;
        }
        //TODO check gravDir normalized
        if (gravDir.Equals(Vector3.zero))
        {
            return false;
        }

        float stepTravelUpHeight = maxStepHeight;
        float stepTraveDownHeight = stepTravelUpHeight;
        float stepSideY = -1f * Vector3.Dot(inHitResult.ImpactNormal, gravDir);
        float pawnInitialFloorBaseY = oldLocation.y - pawnHalfHeight;
        float pawnFloorPointY = pawnInitialFloorBaseY;
        
        if (IsWalkableFloor(currentFloor))
        {
            float floorDist = Mathf.Max(0f, GetDistanceToFloor(currentFloor));
            pawnInitialFloorBaseY -= floorDist;
            stepTravelUpHeight = Mathf.Max(stepTravelUpHeight - floorDist, 0f);
            stepTraveDownHeight = maxStepHeight + MAX_FLOOR_DIST * 2f;
            bool bHitVerticalFace = !IsWithinEdgeTolerance(inHitResult.Location, inHitResult.ImpactPoint, pawnRadius);
            if (currentFloor.bLineTrace == false && bHitVerticalFace == false)
            {
                pawnFloorPointY = currentFloor.hitResult.ImpactPoint.y;
            }
            else
            {
                pawnFloorPointY -= currentFloor.floorDist;
            }
        }

        if (initialImpactY <= pawnInitialFloorBaseY)
        {
            return false;
        }

        //Step up treat as vertical wall
        HitResult sweepUpHit = new HitResult();
        sweepUpHit.time = 1f;
        Quaternion pawnRotation = transform.rotation;
        MoveUpdateImpl(-gravDir * stepTravelUpHeight, pawnRotation, true, sweepUpHit);
        if (sweepUpHit.bStartPenetrating)
        {
            return false;
        }

        HitResult hit = new HitResult();
        hit.time = 1f;
        MoveUpdateImpl(delta, pawnRotation, true, sweepUpHit);

        if (hit.bBlockingHit)
        {
            if (hit.bStartPenetrating)
            {
                return false;
            }

            if (sweepUpHit.bBlockingHit && hit.bBlockingHit)
            {
                //handleImpact sweepUpHit
            }

            //handleImpact sweepUpHit

            float forwardHitTime = hit.time;
            //slideAlongSurface
        }

        return false;
    }

    bool CanStepUp(HitResult inHitResult)
    {
        return false;
    }

    bool IsWalkableFloor(FindFloorResult findFloorResult)
    {
        return findFloorResult.bBlockingHit && findFloorResult.bWalkableFloor;
    }

    float GetDistanceToFloor(FindFloorResult findFloorResult)
    {
        return findFloorResult.bLineTrace ? findFloorResult.lineDist : findFloorResult.floorDist;
    }

    bool IsWithinEdgeTolerance(Vector3 capsuleLocation, Vector3 testImpactPoint, float capsuleRaius)
    {
        float distFromCenterSq = testImpactPoint.x * capsuleLocation.x + testImpactPoint.z * capsuleLocation.z;
        float reduceRadius = Mathf.Max(SWEEP_EDGE_REJECT_DIST + KINDA_SMALL_NUMBER, capsuleRaius - SWEEP_EDGE_REJECT_DIST);
        float reduceRadiusSq = reduceRadius * reduceRadius;
        return distFromCenterSq < reduceRadiusSq;
    }

    Vector3 ConstrainDirectionToPlane(Vector3 direction)
    {
        if (bConstrainToPlane)
        {
            direction = Vector3.ProjectOnPlane(direction, planeConstraintNormal);
        }
        return direction;
    }

    bool MoveUpdateImpl(Vector3 delta, Quaternion newRotation, bool bSweep, HitResult outHit)
    {
        Vector3 NewDelta = ConstrainDirectionToPlane(delta);
        return MoveUpdate(NewDelta, newRotation, bSweep, outHit);
    }

    bool MoveUpdate(Vector3 delta, Quaternion newRotation, bool bSweep, HitResult outHit)
    {
        outHit = new HitResult();
        outHit.time = 1.0f;

        Vector3 traceStart = transform.position;
        Vector3 traceEnd = traceStart + delta;
        float deltaSizeSq = (traceEnd - traceStart).sqrMagnitude;
        Quaternion initalRotationQuat = transform.rotation;
        float minMovementDisSq = bSweep ? Square(4.0f * KINDA_SMALL_NUMBER) : 0f;
        if (deltaSizeSq <= minMovementDisSq)
        {
            if (QuatEquals(newRotation, initalRotationQuat, QUAT_TOLERANCE))
            {
                outHit.TraceStart = traceStart;
                outHit.TraceEnd = traceEnd;

                return true;
            }
            deltaSizeSq = 0f;
        }

        HitResult blockingHit = new HitResult();
        blockingHit.bBlockingHit = false;
        blockingHit.time = 1f;

        bool bFilledHitResult = false;
        bool bMoved = false;
        bool bIncludesOverlapsAtEnd = false;
        bool bRotationOnly = false;
        List<OverlapInfo> pendingOverlaps;
        Transform actor = transform;

        List<HitResult> hits = new List<HitResult>();
        Vector3 newLocation = traceStart;
        if (deltaSizeSq > 0f)
        {
            RaycastHit[] raycastHits = Physics.CapsuleCastAll(traceStart, traceEnd, pawnRadius, delta.normalized);
            foreach (RaycastHit raycastHit in raycastHits)
            {
                HitResult hitResult = new HitResult();
                hitResult.raycastHit = raycastHit;
                hits.Add(hitResult);
            }
            bool bHadBlockingHit = hits.Count > 0;
            if (bHadBlockingHit)
            {
                float delataSize = Mathf.Sqrt(deltaSizeSq);
                for (int i = 0; i < hits.Count; i++)
                {
                    HitResult hit = hits[i];
                    PullBackHit(hit, traceStart, traceEnd, delataSize);
                }
            }

            int firstNonInitialOverlapInx = 0;
            if (bHadBlockingHit)
            {
                int blockintHitIndex = 0;
                float blockingHitNormalDotDelta = BIG_NUMBER;
                for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
                {
                    HitResult testHit = hits[hitIndex];
                    if (testHit.bBlockingHit)
                    {
                        if (testHit.time <= 0)
                        {
                            float normalDotDelta = Vector3.Dot(testHit.ImpactNormal, delta);
                            if (normalDotDelta < blockingHitNormalDotDelta)
                            {
                                blockingHitNormalDotDelta = normalDotDelta;
                                blockintHitIndex = hitIndex;
                            }
                        }
                        else if (blockintHitIndex <= 0)
                        {
                            blockintHitIndex = hitIndex;
                            break;
                        }
                    }
                }

                if (blockintHitIndex >= 0)
                {
                    blockingHit = hits[blockintHitIndex];
                    bFilledHitResult = true;
                }
            }
            if (blockingHit.bBlockingHit == false)
            {
                newLocation = traceEnd;
            }
            else
            {
                newLocation = traceStart + blockingHit.time * (traceEnd - traceStart);
                Vector3 toNewLocation = newLocation - traceStart;
                if (toNewLocation.sqrMagnitude <= minMovementDisSq)
                {
                    newLocation = traceStart;
                    blockingHit.time = 0f;
                }
            }
        }

        if (bFilledHitResult)
        {
            outHit = blockingHit;
        }
        else
        {
            outHit.TraceStart = traceStart;
            outHit.TraceEnd = traceEnd;
        }

        return false;
    }

    float Square(float Value)
    {
        return Value * Value;
    }

    bool QuatEquals(Quaternion rot1, Quaternion rot2, float tolerance)
    {
        bool param1 = Mathf.Abs(rot1.x - rot2.x) <= tolerance && Mathf.Abs(rot1.y - rot2.y) <= tolerance && Mathf.Abs(rot1.z - rot2.z) <= tolerance && Mathf.Abs(rot1.w - rot2.w) <= tolerance;
        bool param2 = Mathf.Abs(rot1.x + rot2.x) <= tolerance && Mathf.Abs(rot1.y + rot2.y) <= tolerance && Mathf.Abs(rot1.z + rot2.z) <= tolerance && Mathf.Abs(rot1.w + rot2.w) <= tolerance;

        return param1 || param2;
    }

    void PullBackHit(HitResult hit, Vector3 start, Vector3 end, float dist)
    {
        float desiredTimeBack = Mathf.Clamp(0.1f, 0.1f / dist, 1.0f / dist) + 0.001f;
        hit.time = Mathf.Clamp(hit.time - desiredTimeBack, 0f, 1f);
    }

    float SlideAlongSurface(Vector3 delta, float time, Vector3 inNormal, HitResult hit, bool bHandleImpact)
    {
        if (hit.bBlockingHit == false)
        {
            return 0f;
        }

        Vector3 normal = inNormal;
        
        if (normal.z > 0f)
        {
            if (IsWalkable(hit) == false)
            {
                normal = normal.normalized;
            }
        }
        else if (normal.z < -KINDA_SMALL_NUMBER)
        {
            if (currentFloor.floorDist < MIN_FLOOR_DIST && currentFloor.bBlockingHit)
            {
                Vector3 floorNormal = currentFloor.hitResult.Normal;
                float deltaDot = Vector3.Dot(delta, floorNormal);
                bool bFloorOpposedToMovement = deltaDot < 0f && floorNormal.z < 1.0f - DELTA;
                if (bFloorOpposedToMovement)
                {
                    normal = floorNormal;
                }

                normal = normal.normalized;
            }
        }
        return -1f;
        //SlideAlongSurface
    }

    bool IsWalkable(HitResult hit)
    {
        if (hit.ImpactNormal.z < KINDA_SMALL_NUMBER)
        {
            return false;
        }

        float testWalkableZ = WalkableFloorY;
        if (hit.ImpactNormal.z < testWalkableZ)
        {
            return false;//too steep
        }

        return true;
    }
}


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


struct StepDownResult
{
}

struct FindFloorResult
{
    public bool bBlockingHit;
    public bool bWalkableFloor;
    public bool bLineTrace;
    public float floorDist;
    public float lineDist;

    public HitResult hitResult;
}

struct OverlapInfo
{
    bool bFromSweep;
    HitResult overlapInfo;
}