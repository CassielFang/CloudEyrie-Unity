namespace CloudEyrie
{
    /// <summary>
    /// 灵伴形态状态（状态模式）：每个形态内聚自己的移动与灵炁消耗。
    ///
    /// 加新形态 = 加一个实现类并在 <see cref="CompanionStateMachine"/> 的 map 里注册，
    /// **不碰现有代码**。
    /// </summary>
    public interface ICompanionForm
    {
        CompanionForm Form { get; }

        /// <summary>每帧移动：读 <c>ctx.Input</c>，写 <c>ctx.Rigidbody.linearVelocity</c>。</summary>
        void Update(float dt, FormContext ctx);

        /// <summary>每秒灵炁消耗</summary>
        float DrainPerSecond();
    }
}
