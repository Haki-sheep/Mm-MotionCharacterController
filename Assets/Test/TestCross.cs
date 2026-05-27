using UnityEngine;

// 脚本功能：在 Scene 视图中绘制 transform.position 的十字线并输出坐标
/// <summary>
/// 验证：transform.position 对应物体的 Pivot 还是 Center
/// 脚本挂载到任意物体上即可
/// </summary>
public class TestTransformPosition : MonoBehaviour
{
    [Header("十字线长度")]
    public float lineLength = 0.5f;

    void Update()
    {
        // 1. 实时打印物体的世界坐标
        Debug.LogWarning($"物体真实坐标(transform.position)：{transform.position}", this);

        // 2. 【核心】在Scene视图绘制彩色十字，标记 transform.position 的真实位置
        // 红色X轴 + 绿色Y轴 + 蓝色Z轴
        Debug.DrawLine(transform.position, transform.position + Vector3.right * lineLength, Color.red);
        Debug.DrawLine(transform.position, transform.position + Vector3.left * lineLength, Color.red);
        Debug.DrawLine(transform.position, transform.position + Vector3.up * lineLength, Color.green);
        Debug.DrawLine(transform.position, transform.position + Vector3.down * lineLength, Color.green);
        Debug.DrawLine(transform.position, transform.position + Vector3.forward * lineLength, Color.blue);
        Debug.DrawLine(transform.position, transform.position + Vector3.back * lineLength, Color.blue);
    }
}
