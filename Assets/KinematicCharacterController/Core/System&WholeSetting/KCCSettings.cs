using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KinematicCharacterController
{
    [CreateAssetMenu]
    public class KCCSettings : ScriptableObject
    {

        /// <summary>
        /// 决定系统是否自动执行模拟若为true，模拟将在FixedUpdate（固定更新）中执行
        /// </summary>
        [Tooltip("决定系统是否自动执行模拟若为true，模拟将在FixedUpdate（固定更新）中执行")]
        public bool AutoSimulation = true;
        

        /// <summary>
        /// 是否需要处理角色和PhysicsMovers（物理移动器）的插值效果
        /// </summary>
        [Tooltip("是否需要处理角色和PhysicsMovers（物理移动器）的插值效果")]
        public bool Interpolate = true;
        
        
        /// <summary>
        /// 系统马达列表的初始容量（容量不足时会自动扩容，设置较高的初始容量可避免GC（垃圾回收）分配）
        /// </summary>
        [Tooltip("系统马达列表的初始容量（容量不足时会自动扩容，\n 设置较高的初始容量可避免GC（垃圾回收）分配）")]
        public int MotorsListInitialCapacity = 100;
        

        /// <summary>
        /// 系统移动器列表的初始容量（容量不足时会自动扩容，设置较高的初始容量可避免GC（垃圾回收）分配）
        /// </summary>
        [Tooltip("系统移动器列表的初始容量（容量不足时会自动扩容，\n 设置较高的初始容量可避免GC（垃圾回收）分配）")]
        public int MoversListInitialCapacity = 100;
    }
}