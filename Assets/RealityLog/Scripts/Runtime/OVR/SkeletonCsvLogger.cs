# nullable enable

using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RealityLog.OVR
{
    public enum SkeletonLogSource
    {
        LeftHand,
        RightHand,
        Body
    }

    public class SkeletonCsvLogger : MonoBehaviour
    {
        private static readonly string[] Header =
            {
                "unix_time", "ovr_timestamp",
                "source", "skeleton_type",
                "is_data_valid", "is_data_high_confidence",
                "provider_is_tracked", "provider_confidence", "provider_scale",
                "thumb_pinch_strength", "index_pinch_strength", "middle_pinch_strength", "ring_pinch_strength", "pinky_pinch_strength",
                "bone_index", "bone_id", "parent_bone_index",
                "pos_x", "pos_y", "pos_z",
                "rot_x", "rot_y", "rot_z", "rot_w"
            };

        [SerializeField] private SkeletonLogSource source = SkeletonLogSource.LeftHand;
        [SerializeField] private string fileName = "skeleton_joints.csv";
        [SerializeField] private string directoryName = "";
        [SerializeField] private bool startLoggingOnStart = false;
        [SerializeField] private bool createRuntimeSkeleton = true;
        [SerializeField] private bool preferOpenXRHandSkeleton = true;
        [Header("Optional")]
        [SerializeField] private OVRSkeleton? skeleton = default;
        [SerializeField] private OVRHand? hand = default;
        [SerializeField] private OVRBody? body = default;
        [SerializeField] private Transform trackingSpace = default!;

        private readonly OvrTimestampConverter timestampConverter = new();
        private CsvWriter? writer = null;
        private double latestTimestamp;
        private bool warnedMissingSkeleton;
        private bool warnedUnavailableData;

        public string DirectoryName
        {
            get => directoryName;
            set => directoryName = value;
        }

        public void StartLogging()
        {
            try
            {
                StopLogging();
                EnsureSkeletonSource();

                var path = Path.Combine(Application.persistentDataPath, DirectoryName, fileName);
                writer = new CsvWriter(path, Header);
                latestTimestamp = 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to create skeleton CsvWriter: {ex.Message}");
                writer = null;
            }
        }

        public void StopLogging()
        {
            try
            {
                writer?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to dispose skeleton CsvWriter: {ex.Message}");
            }

            writer = null;
        }

        private void Awake()
        {
            timestampConverter.Reset();
            EnsureSkeletonSource();
        }

        private void Start()
        {
            if (startLoggingOnStart)
            {
                StartLogging();
            }
        }

        private void LateUpdate()
        {
            if (writer == null)
            {
                return;
            }

            EnsureSkeletonSource();

            if (skeleton == null)
            {
                WarnMissingSkeleton();
                return;
            }

            if (!skeleton.IsInitialized || skeleton.Bones == null || skeleton.Bones.Count == 0)
            {
                WarnUnavailableData("skeleton is not initialized");
                return;
            }

            if (!skeleton.IsDataValid)
            {
                WarnUnavailableData("skeleton data is not valid");
                return;
            }

            var timestamp = OVRPlugin.GetTimeInSeconds();
            if (timestamp <= latestTimestamp)
            {
                return;
            }

            latestTimestamp = timestamp;
            EnqueueSkeletonRows(writer, timestamp);
        }

        private void EnqueueSkeletonRows(CsvWriter csvWriter, double timestamp)
        {
            var unixTime = timestampConverter.ConvertOvrSecToUnixTimeMs(timestamp).ToString();
            var ovrTime = timestamp.ToString();
            var sourceName = source.ToString();
            var skeletonType = skeleton?.GetSkeletonType().ToString() ?? "";
            var dataValid = skeleton?.IsDataValid.ToString() ?? "";
            var highConfidence = skeleton?.IsDataHighConfidence.ToString() ?? "";
            var providerTracked = GetProviderTracked();
            var providerConfidence = GetProviderConfidence();
            var providerScale = GetProviderScale();
            var thumbPinch = GetPinchStrength(OVRHand.HandFinger.Thumb);
            var indexPinch = GetPinchStrength(OVRHand.HandFinger.Index);
            var middlePinch = GetPinchStrength(OVRHand.HandFinger.Middle);
            var ringPinch = GetPinchStrength(OVRHand.HandFinger.Ring);
            var pinkyPinch = GetPinchStrength(OVRHand.HandFinger.Pinky);
            var bones = skeleton!.Bones;

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                var boneTransform = bone.Transform;

                if (boneTransform == null)
                {
                    continue;
                }

                var position = boneTransform.position;
                var rotation = boneTransform.rotation;

                if (trackingSpace != null)
                {
                    position = trackingSpace.TransformPoint(position);
                    rotation = trackingSpace.rotation * rotation;
                }

                csvWriter.EnqueueRow(
                    unixTime, ovrTime,
                    sourceName, skeletonType,
                    dataValid, highConfidence,
                    providerTracked, providerConfidence, providerScale,
                    thumbPinch, indexPinch, middlePinch, ringPinch, pinkyPinch,
                    i.ToString(), bone.Id.ToString(), bone.ParentBoneIndex.ToString(),
                    position.x.ToString(), position.y.ToString(), position.z.ToString(),
                    rotation.x.ToString(), rotation.y.ToString(), rotation.z.ToString(), rotation.w.ToString()
                );
            }
        }

        private void EnsureSkeletonSource()
        {
            if (skeleton != null)
            {
                return;
            }

            if (!createRuntimeSkeleton)
            {
                skeleton = GetComponent<OVRSkeleton>();
                return;
            }

            switch (source)
            {
                case SkeletonLogSource.LeftHand:
                case SkeletonLogSource.RightHand:
                    EnsureHandSkeleton();
                    break;
                case SkeletonLogSource.Body:
                    EnsureBodySkeleton();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void EnsureHandSkeleton()
        {
            hand ??= GetComponent<OVRHand>();
            hand ??= gameObject.AddComponent<OVRHand>();

            var handType = source == SkeletonLogSource.LeftHand ? OVRHand.Hand.HandLeft : OVRHand.Hand.HandRight;
            var controller = source == SkeletonLogSource.LeftHand ? OVRInput.Controller.LHand : OVRInput.Controller.RHand;

            TrySetMember(hand, new[] { "HandType", "_handType", "handType" }, handType);
            TrySetMember(hand, new[] { "Controller", "_controller", "controller" }, controller);

            skeleton = GetComponent<OVRSkeleton>();
            skeleton ??= gameObject.AddComponent<OVRSkeleton>();

            var skeletonType = source == SkeletonLogSource.LeftHand
                ? preferOpenXRHandSkeleton ? OVRSkeleton.SkeletonType.XRHandLeft : OVRSkeleton.SkeletonType.HandLeft
                : preferOpenXRHandSkeleton ? OVRSkeleton.SkeletonType.XRHandRight : OVRSkeleton.SkeletonType.HandRight;

            ConfigureSkeleton(skeleton, skeletonType, hand);
        }

        private void EnsureBodySkeleton()
        {
            body ??= GetComponent<OVRBody>();
            body ??= gameObject.AddComponent<OVRBody>();

            RequestFullBodyJointSet(body);

            skeleton = GetComponent<OVRSkeleton>();
            skeleton ??= gameObject.AddComponent<OVRSkeleton>();

            ConfigureSkeleton(skeleton, OVRSkeleton.SkeletonType.FullBody, body);
        }

        private static void ConfigureSkeleton(OVRSkeleton targetSkeleton, OVRSkeleton.SkeletonType skeletonType, object provider)
        {
            TrySetMember(targetSkeleton, new[] { "_skeletonType", "skeletonType", "SkeletonType" }, skeletonType);
            TrySetMember(targetSkeleton, new[] { "_dataProvider", "dataProvider", "DataProvider", "_skeletonDataProvider" }, provider);
        }

        private static void RequestFullBodyJointSet(OVRBody targetBody)
        {
            var bodyJointSetType = typeof(OVRPlugin).GetNestedType("BodyJointSet");
            if (bodyJointSetType == null)
            {
                return;
            }

            var jointSet = ParseEnumValue(bodyJointSetType, "FullBody")
                ?? ParseEnumValue(bodyJointSetType, "FullBodyWithHands")
                ?? ParseEnumValue(bodyJointSetType, "Body");

            if (jointSet == null)
            {
                return;
            }

            TrySetMember(targetBody, new[] { "_providedSkeletonType", "providedSkeletonType", "ProvidedSkeletonType" }, jointSet);

            var setRequestedJointSet = typeof(OVRBody).GetMethod(
                "SetRequestedJointSet",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            setRequestedJointSet?.Invoke(null, new[] { jointSet });
        }

        private static object? ParseEnumValue(Type enumType, string name)
        {
            try
            {
                return Enum.Parse(enumType, name);
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySetMember(object target, string[] memberNames, object value)
        {
            var targetType = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var name in memberNames)
            {
                var field = targetType.GetField(name, flags);
                if (field != null && TryConvertValue(field.FieldType, value, out var convertedFieldValue))
                {
                    field.SetValue(target, convertedFieldValue);
                    return true;
                }

                var property = targetType.GetProperty(name, flags);
                if (property is { CanWrite: true } && TryConvertValue(property.PropertyType, value, out var convertedPropertyValue))
                {
                    property.SetValue(target, convertedPropertyValue);
                    return true;
                }
            }

            return false;
        }

        private static bool TryConvertValue(Type targetType, object value, out object? converted)
        {
            converted = null;

            if (value == null)
            {
                return !targetType.IsValueType;
            }

            var valueType = value.GetType();
            if (targetType.IsAssignableFrom(valueType))
            {
                converted = value;
                return true;
            }

            if (targetType.IsEnum && valueType.IsEnum)
            {
                converted = Enum.ToObject(targetType, value);
                return true;
            }

            return false;
        }

        private string GetProviderTracked()
            => hand != null ? hand.IsTracked.ToString() : "";

        private string GetProviderConfidence()
            => hand != null ? hand.HandConfidence.ToString() : "";

        private string GetProviderScale()
            => hand != null ? hand.HandScale.ToString() : "";

        private string GetPinchStrength(OVRHand.HandFinger finger)
            => hand != null ? hand.GetFingerPinchStrength(finger).ToString() : "";

        private void WarnMissingSkeleton()
        {
            if (warnedMissingSkeleton)
            {
                return;
            }

            Debug.LogWarning($"[{Constants.LOG_TAG}] Skeleton logger for {source} has no OVRSkeleton source.");
            warnedMissingSkeleton = true;
        }

        private void WarnUnavailableData(string reason)
        {
            if (warnedUnavailableData)
            {
                return;
            }

            Debug.LogWarning($"[{Constants.LOG_TAG}] Skeleton logger for {source} is waiting because {reason}.");
            warnedUnavailableData = true;
        }

        private void OnDestroy()
        {
            StopLogging();
        }
    }
}
