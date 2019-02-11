using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterStepUpComponent : MonoBehaviour
{
    public float WalkableFloorAngle;
    public float WalkableFloorY;
    public float maxStepHeight;
    public float perchRadiusThreshold;

    public Transform target;
    public float velocity;
    public float PerchAdditionalHeight;

    private CapsuleCollider capsuleCollider;
    private FindFloorResult currentFloor;
    private bool bConstrainToPlane;
    private Vector3 planeConstraintNormal;

    private List<Vector3> debugMovePaths = new List<Vector3>();

    float pawnRadius, pawnHalfHeight;

    private const float SWEEP_EDGE_REJECT_DIST = 0.15f;
    private const float KINDA_SMALL_NUMBER = 1e-4f;
    private const float QUAT_TOLERANCE = 1e-8f;
    private const float BIG_NUMBER = 3.4e+38f;
    private const float MIN_FLOOR_DIST = 1.9f;
    private const float MAX_FLOOR_DIST = 2.4f;
    private const float DELTA = 0.00001f;
    private const float PenetrationPullbackDistance = 0.125f;
    private const float PenetrationOverlapInflation = 0.1f;
    private const float MAX_STEP_SIDE_Y = 0.08f;

    // Start is called before the first frame update
    void Start()
    {
        capsuleCollider = gameObject.GetComponent<CapsuleCollider>();
        pawnRadius = capsuleCollider.radius;
        pawnHalfHeight = capsuleCollider.height / 2;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            HitResult hit = new HitResult();
            hit.time = 1f;
            Quaternion pawnRotation = transform.rotation;

            Vector3 moveForward = (target.position - transform.position).normalized;
            MoveUpdateImpl(moveForward * velocity * Time.deltaTime, pawnRotation, true, out hit);
            if (hit.bBlockingHit)
            {
                Debug.Log("BlockingHit...");
                Vector3 gravDir = Vector3.down;
                StepDownResult stepDownResult;
                StepUp(gravDir, moveForward * velocity * Time.deltaTime, hit, out stepDownResult);
            }
        }
    }

    bool StepUp(Vector3 gravDir, Vector3 delta, HitResult inHitResult, out StepDownResult stepDownResult)
    {
        stepDownResult = new StepDownResult();

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

        //float initialImpactY = inHitResult.ImpactPoint.y;
        //if (initialImpactY > oldLocation.y + (pawnHalfHeight - pawnRadius))
        //{
        //    return false;
        //}
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

        //if (initialImpactY <= pawnInitialFloorBaseY)
        //{
        //    return false;
        //}

        //step up
        HitResult sweepUpHit = new HitResult();
        sweepUpHit.time = 1f;
        Quaternion pawnRotation = transform.rotation;
        MoveUpdateImpl(-gravDir * stepTravelUpHeight, pawnRotation, true, out sweepUpHit);
        if (sweepUpHit.bStartPenetrating)
        {
            return false;
        }

        //step fwd
        HitResult hit = new HitResult();
        hit.time = 1f;
        MoveUpdateImpl(delta, pawnRotation, true, out sweepUpHit);

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
            float forwardSlideAmount = SlideAlongSurface(delta, 1f - hit.time, hit.Normal, hit, true);

            if (forwardHitTime == 0f && forwardSlideAmount == 0f)
            {
                return false;
            }
            //slideAlongSurface
        }

        //step down
        MoveUpdate(gravDir * stepTraveDownHeight, transform.rotation, true, out hit);
        if (hit.bStartPenetrating)
        {
            return false;
        }

        if (hit.bBlockingHit && hit.bStartPenetrating == false)
        {
            float deltaY = hit.ImpactPoint.y - pawnFloorPointY;
            if (deltaY > maxStepHeight)
            {
                return false;
            }

            if (IsWalkable(hit) == false)
            {
                bool bNormalTowards = Vector3.Dot(delta, hit.ImpactNormal) < 0f;
                if (bNormalTowards)
                {
                    return false;
                }

                if (hit.Location.y > oldLocation.y)
                {
                    return false;
                }
            }

            if (IsWithinEdgeTolerance(transform.position, hit.Location, pawnRadius) == false)
            {
                return false;
            }

            //todo can step up check
            if (deltaY > 0f)
            {
                return false;
            }

            FindFloor(transform.position, stepDownResult.FloorResult, false, hit);
            if (hit.Location.z > oldLocation.z)
            {
                if (stepDownResult.FloorResult.bBlockingHit && stepSideY < MAX_STEP_SIDE_Y)
                {
                    return false;
                }
            }

            stepDownResult.bComputedFloor = true;
        }

        return true;
    }

    bool ShouldComputePerchResult(HitResult inHit, bool bCheckRadius)
    {
        if (inHit.bBlockingHit && inHit.bStartPenetrating == false)
        {
            return false;
        }

        perchRadiusThreshold = Mathf.Max(0f, perchRadiusThreshold);
        if (perchRadiusThreshold <= SWEEP_EDGE_REJECT_DIST)
        {
            return false;
        }

        if (bCheckRadius)
        {
            float distFromCenterSq = SizeSquared2D(inHit.ImpactPoint, inHit.Location);
            float standOnEdgeRadius = GetValidPerchRadius();
            if (distFromCenterSq <= Square(standOnEdgeRadius))
            {
                return false;
            }
        }

        return true;
    }

    float GetValidPerchRadius()
    {
        return Mathf.Clamp(pawnRadius - perchRadiusThreshold, 0.1f, pawnRadius);
    }

    void FindFloor(Vector3 capsuleLocation, FindFloorResult outFindFloorResult, bool bCanUseCachedLocation, HitResult downwardSweepResult)
    {
        //TODO check isMovingOnGround
        bool isMovingOnGround = false;
        float heightCheckAdjust = isMovingOnGround ? (MAX_FLOOR_DIST + KINDA_SMALL_NUMBER) : -MAX_FLOOR_DIST;
        float floorSweepTraceDist = Mathf.Max(MAX_FLOOR_DIST, maxStepHeight + heightCheckAdjust);
        float floorLineTraceDist = floorSweepTraceDist;
        bool bNeedToValidateFloor = false;
        bool bAlwaysCheckFloor = true;
        if (floorLineTraceDist > 0f || floorSweepTraceDist > 0f)
        {
            if (bAlwaysCheckFloor)
            {
                ComputerFloorDist(capsuleLocation, floorLineTraceDist, floorSweepTraceDist, outFindFloorResult, pawnRadius, downwardSweepResult);
            }
        }

        if (bNeedToValidateFloor && outFindFloorResult.bBlockingHit && outFindFloorResult.bLineTrace == false)
        {
            bool bCheckRadius = true;
            if (ShouldComputePerchResult(outFindFloorResult.hitResult, bCheckRadius))
            {
                float MaxPerchFloorDist = Mathf.Max(MAX_FLOOR_DIST, maxStepHeight + heightCheckAdjust);
                MaxPerchFloorDist += Mathf.Max(0f, PerchAdditionalHeight);

                FindFloorResult perchFloorResult = new FindFloorResult();
                float PerchRadius = GetValidPerchRadius();
                if (ComputePerchResult(PerchRadius, outFindFloorResult.hitResult, MaxPerchFloorDist, perchFloorResult))
                {
                    float avgFloorDist = (MIN_FLOOR_DIST + MAX_FLOOR_DIST) / 2f;
                    float moveUpDist = (avgFloorDist - outFindFloorResult.floorDist);
                    if (moveUpDist + perchFloorResult.floorDist >= MaxPerchFloorDist)
                    {
                        outFindFloorResult.floorDist = avgFloorDist;
                    }

                    if (outFindFloorResult.bWalkableFloor == false)
                    {
                        if (outFindFloorResult.bBlockingHit && perchFloorResult.hitResult.bBlockingHit)
                        {
                            perchFloorResult.hitResult.time = outFindFloorResult.hitResult.time;
                            perchFloorResult.hitResult.ImpactPoint = outFindFloorResult.hitResult.ImpactPoint;
                            perchFloorResult.hitResult.Location = outFindFloorResult.hitResult.Location;
                            perchFloorResult.hitResult.TraceStart = outFindFloorResult.hitResult.TraceStart;
                            perchFloorResult.hitResult.TraceEnd = outFindFloorResult.hitResult.TraceEnd;

                            outFindFloorResult.hitResult = perchFloorResult.hitResult;
                            outFindFloorResult.bLineTrace = true;
                            outFindFloorResult.floorDist = perchFloorResult.floorDist;
                            outFindFloorResult.lineDist = perchFloorResult.lineDist;
                            outFindFloorResult.bWalkableFloor = perchFloorResult.bWalkableFloor;
                        }
                    }
                }
                else
                {
                    outFindFloorResult.bWalkableFloor = false;
                }
            }
        }
    }

    bool ComputePerchResult(float testRadius, HitResult inHit, float inMaxFloorDist, FindFloorResult outPerchFloorResult)
    {
        if (inMaxFloorDist <= 0f)
        {
            return false;
        }
        float inHitAboveBase = Mathf.Max(0f, inHit.ImpactPoint.z - (inHit.Location.z - pawnHalfHeight));
        float perchLineDist = Mathf.Max(0f, inMaxFloorDist - inHitAboveBase);
        float perchSweepDist = Mathf.Max(0f, inMaxFloorDist);

        float actualSweepDist = perchSweepDist + pawnRadius;
        HitResult downwardSweepResult = new HitResult();
        ComputerFloorDist(inHit.Location, perchSweepDist, actualSweepDist, outPerchFloorResult, testRadius, downwardSweepResult);
        if ((outPerchFloorResult.bWalkableFloor && outPerchFloorResult.bBlockingHit) == false)
        {
            return false;
        }
        else if (inHitAboveBase + outPerchFloorResult.floorDist > inMaxFloorDist)
        {
            outPerchFloorResult.bWalkableFloor = false;
            return false;
        }

        return true;
    }

    void ComputerFloorDist(Vector3 capsuleLocation, float lineDistance, float sweepDistance, FindFloorResult outFloorResult, float sweepRadius, HitResult downwardSweepResult)
    {
        pawnRadius = capsuleCollider.radius;
        pawnHalfHeight = capsuleCollider.height / 2;
        bool bSkipSweep = false;
        if (downwardSweepResult.bBlockingHit && downwardSweepResult.bStartPenetrating == false)
        {
            if ((downwardSweepResult.TraceStart.z > downwardSweepResult.TraceEnd.z) &&
               SizeSquared2D(downwardSweepResult.TraceStart, downwardSweepResult.TraceEnd) <= KINDA_SMALL_NUMBER)
            {
                if (IsWithinEdgeTolerance(downwardSweepResult.Location, downwardSweepResult.ImpactPoint, pawnRadius))
                {
                    bSkipSweep = true;
                    bool bIsWalkable = IsWalkable(downwardSweepResult);
                    float floorDist = capsuleLocation.y - downwardSweepResult.Location.y;
                    outFloorResult = SetFromSweep(downwardSweepResult, floorDist, bIsWalkable);
                    if (bIsWalkable)
                    {
                        return;
                    }
                }
            }

            if (sweepDistance < lineDistance)
            {
                return;
            }
        }

        bool bBlockingHit = false;
        if (bSkipSweep == false && sweepDistance > 0f && sweepRadius > 0f)
        {
            float shrinkScale = 0.9f;
            float shrinkScaleOverlap = 0.1f;
            float shrinkHeight = (pawnHalfHeight - pawnRadius) * (1f - shrinkScale);
            float traceDist = sweepDistance + shrinkHeight;
            //MakeCapsule
            float newPawnRadius = sweepRadius;
            float newPawnHalfHeight = pawnHalfHeight - shrinkHeight;
            HitResult hit = new HitResult();
            bBlockingHit = FloorSweepTest(hit, capsuleLocation, newPawnRadius, newPawnHalfHeight, traceDist, Vector3.down * traceDist);
            if (bBlockingHit)
            {
                if (hit.bStartPenetrating || IsWithinEdgeTolerance(capsuleLocation, hit.ImpactPoint, newPawnRadius) == false)
                {
                    newPawnRadius = Mathf.Max(0f, newPawnRadius - SWEEP_EDGE_REJECT_DIST - KINDA_SMALL_NUMBER);
                    if (newPawnRadius > KINDA_SMALL_NUMBER)
                    {
                        shrinkHeight = (pawnHalfHeight - pawnRadius) * (1f - shrinkScaleOverlap);
                        traceDist = sweepDistance + shrinkHeight;
                        newPawnHalfHeight = Mathf.Max(pawnHalfHeight - shrinkHeight, newPawnRadius);
                        hit = new HitResult();
                        hit.time = 1.0f;
                        bBlockingHit = FloorSweepTest(hit, capsuleLocation, newPawnRadius, newPawnHalfHeight, traceDist, Vector3.down * traceDist);
                    }
                }

                float maxPenetrationAdjust = Mathf.Max(MAX_FLOOR_DIST, pawnRadius);
                float sweepResult = Mathf.Max(-maxPenetrationAdjust, hit.time * traceDist - shrinkHeight);

                outFloorResult = SetFromSweep(hit, sweepResult, false);

                if (hit.bBlockingHit && hit.bStartPenetrating == false && IsWalkable(hit))
                {
                    if (sweepResult <= sweepDistance)
                    {
                        outFloorResult.bWalkableFloor = true;
                        return;
                    }
                }
            }
        }

        if (outFloorResult.bBlockingHit == false && outFloorResult.hitResult.bStartPenetrating == false)
        {
            outFloorResult.floorDist = sweepDistance;
            return;
        }

        if (lineDistance > 0f)
        {
            float shrinkHeight = pawnHalfHeight;
            Vector3 lineTraceStart = capsuleLocation;
            float traceDist = lineDistance + shrinkHeight;
            Vector3 down = Vector3.down * traceDist;
            HitResult hit = new HitResult();
            hit.time = 1f;

            bBlockingHit = Physics.Raycast(lineTraceStart, Vector3.down, traceDist);

            if (bBlockingHit)
            {
                if (hit.time > 0f)
                {
                    float maxPenetrationAdjust = Mathf.Max(MAX_FLOOR_DIST, pawnRadius);
                    float lineResult = Mathf.Max(-maxPenetrationAdjust, hit.time * traceDist - shrinkHeight);

                    outFloorResult.bBlockingHit = true;
                    if (lineResult <= lineDistance && IsWalkable(hit))
                    {
                        outFloorResult = SetFromLineTrace(outFloorResult, hit, outFloorResult.floorDist, true, lineResult);
                        return;
                    }
                }
            }
        }

        outFloorResult.bWalkableFloor = false;
        outFloorResult.floorDist = sweepDistance;
    }

    bool FloorSweepTest(HitResult outHit, Vector3 center, float radius, float hegiht, float distance, Vector3 direction)
    {
        bool bBlockingHit = false;
        Vector3 start = center - Vector3.up * hegiht / 2;
        Vector3 end = center + Vector3.up * hegiht / 2;
        RaycastHit raycastHit;
        bBlockingHit = Physics.CapsuleCast(start, end, radius, direction, out raycastHit);
        
        return bBlockingHit;
    }

    void MakeCapsule(float capsuleRadius, float capsuleHalfHeight)
    {
    }

    FindFloorResult SetFromSweep(HitResult InHit, float inSweepFloorDist, bool bWalkableFloor)
    {
        FindFloorResult findFloorResult = new FindFloorResult();
        findFloorResult.bBlockingHit = InHit.bBlockingHit && InHit.bStartPenetrating == false;
        findFloorResult.bLineTrace = false;
        findFloorResult.floorDist = inSweepFloorDist;
        findFloorResult.lineDist = 0f;
        findFloorResult.hitResult = InHit;

        return findFloorResult;
    }

    FindFloorResult SetFromLineTrace(FindFloorResult oldFindFloorResult, HitResult inHit, float inSweepFloorDist, bool bWalkableFloor, float inLineDist)
    {
        FindFloorResult findFloorResult = new FindFloorResult();
        if (inHit.bBlockingHit && oldFindFloorResult.bBlockingHit)
        {
            findFloorResult.hitResult = inHit;
            findFloorResult.hitResult.ImpactPoint = oldFindFloorResult.hitResult.ImpactPoint;
            findFloorResult.hitResult.Location = oldFindFloorResult.hitResult.Location;
            findFloorResult.hitResult.TraceStart = oldFindFloorResult.hitResult.TraceStart;
            findFloorResult.hitResult.TraceEnd = oldFindFloorResult.hitResult.TraceEnd;

            findFloorResult.bLineTrace = true;
            findFloorResult.floorDist = inSweepFloorDist;
            findFloorResult.lineDist = inLineDist;
            findFloorResult.bWalkableFloor = bWalkableFloor;
        }

        return findFloorResult;
    }

    float SizeSquared2D(Vector3 v1, Vector3 v2)
    {
        return v1.x * v2.x + v1.y * v2.y;
    }

    bool CanStepUp(HitResult inHitResult)
    {
        return true;
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
        float distFromCenterSq = Square(Mathf.Abs(testImpactPoint.x - capsuleLocation.x)) + Square(Mathf.Abs(testImpactPoint.z - capsuleLocation.z));
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

    bool MoveUpdateImpl(Vector3 delta, Quaternion newRotation, bool bSweep, out HitResult outHit)
    {
        Vector3 NewDelta = ConstrainDirectionToPlane(delta);
        return MoveUpdate(NewDelta, newRotation, bSweep, out outHit);
    }

    bool MoveUpdate(Vector3 delta, Quaternion newRotation, bool bSweep, out HitResult outHit)
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
            float delataSize = Mathf.Sqrt(deltaSizeSq);

            Vector3 p1 = transform.position + capsuleCollider.center - Vector3.up * pawnHalfHeight + Vector3.up * pawnRadius;
            Vector3 p2 = transform.position + capsuleCollider.center + Vector3.up * pawnHalfHeight - Vector3.up * pawnRadius;
            RaycastHit[] raycastHits = Physics.CapsuleCastAll(p1, p2, pawnRadius, delta.normalized, delataSize);
            foreach (RaycastHit raycastHit in raycastHits)
            {
                HitResult hitResult = new HitResult();
                hitResult.raycastHit = raycastHit;
                hitResult.time = raycastHit.distance / delataSize;
                hitResult.distance = raycastHit.distance;
                hitResult.ImpactPoint = raycastHit.point;
                hitResult.ImpactNormal = raycastHit.normal;
                hitResult.bStartPenetrating = false;
                hitResult.bBlockingHit = raycastHit.distance > 0;
                hitResult.Location = raycastHit.point;
                hits.Add(hitResult);
            }
            bool bHadBlockingHit = hits.Count > 0;
            if (bHadBlockingHit)
            {
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
        transform.position = newLocation;
        debugMovePaths.Add(newLocation);
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

    bool IsNearZero(Vector3 value, float tolerance)
    {
        return Mathf.Abs(value.x) <= tolerance && 
               Mathf.Abs(value.y) <= tolerance && 
               Mathf.Abs(value.z) <= tolerance;
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
        if (hit.bBlockingHit == false)
        {
            return 0f;
        }
        float percentTimeApplied = 0f;
        Vector3 oldHitNormal = normal;

        Vector3 slideDelta = ComputerSlideVector(delta, time, normal, hit);

        if (Vector3.Dot(slideDelta, delta) > 0f)
        {
            Quaternion rotation = transform.rotation;
            SafeMoveUpdatedComponent(slideDelta, rotation, true, hit);

            float firstHitPercent = hit.time;
            percentTimeApplied = firstHitPercent;
          
            if (hit.bBlockingHit && hit.bStartPenetrating == false)
            {
                TwoWallAdjust(slideDelta, hit, oldHitNormal);
                if (IsNearZero(slideDelta, 1e-3f) && Vector3.Dot(slideDelta, delta) > 0f)
                {
                    SafeMoveUpdatedComponent(slideDelta, rotation, true, hit);
                    float secondHitPercent = hit.time * (1f - firstHitPercent);
                    percentTimeApplied += secondHitPercent;
                }
            }

            return Mathf.Clamp(percentTimeApplied, 0f, 1f);
        }

        return 0f;
    }

    void TwoWallAdjust(Vector3 outDelta, HitResult hit, Vector3 oldHitNormal)
    {
        Vector3 delta = outDelta;
        Vector3 hitNormal = hit.Normal;
        if (Vector3.Dot(oldHitNormal, hitNormal) <= 0.0f)
        {
            Vector3 desiredDir = delta;
            Vector3 newDir = Vector3.Cross(hitNormal, oldHitNormal);
            newDir = newDir.normalized;
            delta = Vector3.Dot(delta, newDir) * (1.0f - hit.time) * newDir;
            if (Vector3.Dot(desiredDir, delta) < 0f)
            {
                delta = -1f * delta;
            }
        }
        else
        {
            Vector3 desiredDir = delta;
            delta = ComputerSlideVector(delta, 1f - hit.time, hitNormal, hit);
            if (Vector3.Dot(delta, desiredDir) <= 0f)
            {
                delta = Vector3.zero;
            }
            else if (Mathf.Abs(Vector3.Dot(hitNormal, oldHitNormal)) - 1f < KINDA_SMALL_NUMBER)
            {
                delta += hitNormal * 0.01f;
            }
        }

        outDelta = delta;
    }

    bool SafeMoveUpdatedComponent(Vector3 delta, Quaternion newRotation, bool bSweep, HitResult outHit)
    {
        bool bMoveResult = false;

        bMoveResult = MoveUpdate(delta, newRotation, bSweep, out outHit);
        if (outHit.bStartPenetrating)
        {
            Vector3 requestAdjustment = GetPenetrationAdjustment(outHit);
            if (ResolvePenetration(requestAdjustment, outHit, newRotation))
            {
                bMoveResult = MoveUpdate(delta, newRotation, bSweep, out outHit);
            }
        }
        return bMoveResult;
    }

    bool ResolvePenetration(Vector3 proposeAdjustment, HitResult hit, Quaternion newRotation)
    {
        Vector3 adjustment = ConstrainDirectionToPlane(proposeAdjustment);
        if (adjustment.Equals(Vector3.zero) == false)
        {
            HitResult sweepOutHit = new HitResult();
            sweepOutHit.time = 1.0f;
            float overlapInflation = PenetrationOverlapInflation;
            bool bMoved = MoveUpdate(adjustment, newRotation, true, out sweepOutHit);
            if (bMoved == false && sweepOutHit.bStartPenetrating)
            {
                Vector3 secondMTD = GetPenetrationAdjustment(sweepOutHit);
                Vector3 combinedMTD = adjustment + secondMTD;
                if (secondMTD != adjustment && combinedMTD.Equals(Vector3.zero) == false)
                {
                    HitResult hitResult = new HitResult();
                    bMoved = MoveUpdate(combinedMTD, newRotation, true, out hitResult);
                }
            }
            if (bMoved == false)
            {
                Vector3 moveDelta = ConstrainDirectionToPlane(hit.TraceEnd - hit.TraceStart);
                if (moveDelta.Equals(Vector3.zero) == false)
                {
                    HitResult hitResult = new HitResult();
                    bMoved = MoveUpdate(adjustment + moveDelta, newRotation, true, out hitResult);
                }
            }

            return bMoved;
        }

        return false;
    }

    Vector3 GetPenetrationAdjustment(HitResult outHit)
    {
        if (outHit.bStartPenetrating)
        {
            return Vector3.zero;
        }
        Vector3 Result;
        float pullBackDistance = Mathf.Abs(PenetrationPullbackDistance);
        float penetrationDepth = outHit.PenetrationDepth > 0f ? outHit.PenetrationDepth : 0.125f;

        Result = outHit.Normal * (pullBackDistance + penetrationDepth);

        return ConstrainDirectionToPlane(Result);
    }

    Vector3 ComputerSlideVector(Vector3 delta, float time, Vector3 normal, HitResult hit)
    {
        if (bConstrainToPlane == false)
        {
            return Vector3.ProjectOnPlane(delta, normal) * time;
        }
        else
        {
            Vector3 projectedNormal = ConstrainNormalToPlane(normal);
            return Vector3.ProjectOnPlane(delta, normal) * time;
        }
    }

    Vector3 ConstrainNormalToPlane(Vector3 normal)
    {
        if (bConstrainToPlane)
        {
            normal = Vector3.ProjectOnPlane(normal, planeConstraintNormal).normalized;
        }
        return normal;
    }

    bool IsWalkable(HitResult hit)
    {
        if (hit.ImpactNormal.y < KINDA_SMALL_NUMBER)
        {
            return false;
        }

        float testWalkableY = WalkableFloorY;
        if (hit.ImpactNormal.y < testWalkableY)
        {
            return false;//too steep
        }

        return true;
    }

    private void OnDrawGizmos()
    {
        for (int i = 0; i < debugMovePaths.Count; i++)
        {
            Vector3 debugMovePt = debugMovePaths[i];
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(debugMovePt, 0.3f);
        }
    }
}