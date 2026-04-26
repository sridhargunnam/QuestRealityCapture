# nullable enable

using System;
using System.IO;
using UnityEngine;

namespace RealityLog.OVR
{
    public enum PoseStateMode
    {
        Immediate,
        Raw
    }

    class PoseLogger : MonoBehaviour
    {
        private static readonly string[] HEADER = new string[]
            {
                "unix_time", "ovr_timestamp",
                "pos_x", "pos_y", "pos_z", 
                "rot_x", "rot_y", "rot_z", "rot_w", 
            };

        [SerializeField] private OVRPlugin.Node node = OVRPlugin.Node.Head;
        [SerializeField] private PoseStateMode mode = PoseStateMode.Immediate;
        [SerializeField] private string fileName = "poses.csv";
        [SerializeField] private string directoryName = "";
        [SerializeField] private bool startLoggingOnStart = false;
        [Header("Optional")]
        [SerializeField] private Transform trackingSpace = default!;

        private CsvWriter? writer = null;

        private readonly OvrTimestampConverter timestampConverter = new();

        private double latestTimestamp;

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
                var filePath = Path.Combine(Application.persistentDataPath, DirectoryName, fileName);
                writer = new CsvWriter(filePath, HEADER);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to create CsvWriter: {ex.Message}");
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
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to dispose CsvWriter: {ex.Message}");
            }

            writer = null;
        }

        private void Start()
        {
            timestampConverter.Reset();

            if (startLoggingOnStart)
            {
                StartLogging();
            }
        }

        private void FixedUpdate()
        {
            if (writer == null)
                return;

            EnqueueRowIfNeeded(writer);
        }

        private void EnqueueRowIfNeeded(CsvWriter writer)
        {
            var poseState = mode switch 
                {
                    PoseStateMode.Immediate => OVRPlugin.GetNodePoseStateImmediate(node),
                    PoseStateMode.Raw => OVRPlugin.GetNodePoseStateRaw(node, OVRPlugin.Step.Render),
                    _ => OVRPlugin.PoseStatef.identity,
                };

            var timestamp = poseState.Time;

            if (timestamp <= latestTimestamp)
            {
                return;
            }

            latestTimestamp = timestamp;

            var pose = poseState.Pose.ToOVRPose();

            var position = pose.position;
            var orientation = pose.orientation;

            if (trackingSpace != null)
            {
                position = trackingSpace.TransformPoint(position);
                orientation = trackingSpace.rotation * orientation;
            }

            writer.EnqueueRow(
                timestampConverter.ConvertOvrSecToUnixTimeMs(timestamp), timestamp,
                position.x, position.y, position.z,
                orientation.x, orientation.y, orientation.z, orientation.w
            );
        }

        private void OnDestroy()
        {
            writer?.Dispose();
            writer = null;
        }
    }
}