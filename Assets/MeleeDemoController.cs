using UnityEngine;
using System.Collections;

/// <summary>
/// 第三人称近战 Demo 控制器 (动画状态限制版)
/// 实现：必须等跳跃动画彻底结束才能再次起跳
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class MeleeDemoController : MonoBehaviour
{
    [Header("角色属性")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 8f;
    public float jumpHeight = 1.5f;
    public float gravity = -20.0f; // 大重力，手感更实
    public float turnSmoothTime = 0.1f;

    // 注意：我们移除了手动的冷却时间，改为完全依赖动画状态
    // public float jumpCooldown = 0.5f; // 已移除

    [Header("战斗设置")]
    public float attackRange = 1.5f;
    public float attackCooldown = 0.5f;
    public LayerMask enemyLayers;

    [Header("动画组件")]
    public Animator animator;

    [Header("相机设置")]
    public Transform cameraTransform;
    public float cameraDistance = 5f;
    public float mouseSensitivity = 2f;
    public Vector2 pitchLimits = new Vector2(-10, 60);

    // 私有变量
    private CharacterController _controller;
    private Vector3 _velocity;
    private float _turnSmoothVelocity;
    private bool _isGrounded;
    private bool _canAttack = true;
    private float _yaw = 0f;
    private float _pitch = 0f;

    void Start()
    {
        _controller = GetComponent<CharacterController>();
        // 自动获取动画组件
        if (animator == null) animator = GetComponentInChildren<Animator>();
        // 自动获取相机
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        // 锁定鼠标
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Vector3 angles = transform.eulerAngles;
        _yaw = angles.y;
    }

    void Update()
    {
        // 1. 地面检测
        _isGrounded = _controller.isGrounded;
        if (_isGrounded && _velocity.y < 0) _velocity.y = -2f;

        // 更新动画参数 IsGrounded (配合之前的 Animator 设置)
        if (animator != null) animator.SetBool("IsGrounded", _isGrounded);

        // 2. 核心逻辑
        HandleCameraOrbit();
        HandleMovement();
        HandleAttack();

        // 3. 重力
        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }

    void LateUpdate()
    {
        UpdateCameraPosition();
    }

    void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 dir = new Vector3(h, 0f, v).normalized;

        // --- 移动处理 ---
        if (dir.magnitude >= 0.1f)
        {
            bool isSprinting = Input.GetKey(KeyCode.LeftShift);
            float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;

            float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            _controller.Move(moveDir.normalized * currentSpeed * Time.deltaTime);

            if (animator != null) animator.SetFloat("Speed", isSprinting ? 1f : 0.5f, 0.1f, Time.deltaTime);
        }
        else
        {
            if (animator != null) animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
        }

        // --- 跳跃逻辑 (核心修改) ---

        bool isJumpAnimationFinished = true;

        if (animator != null)
        {
            // 获取第0层（Base Layer）的当前状态信息
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            // 判断条件：
            // 1. IsName("Jump"): 正在播放 Jump 状态
            // 2. IsInTransition(0): 正在进行状态切换 (比如从 Jump 切回 Idle 的那 0.1秒)
            // 如果满足任一条件，说明跳跃动作还没彻底完事
            if (stateInfo.IsName("Jump") || animator.IsInTransition(0))
            {
                isJumpAnimationFinished = false;
            }
        }

        // 只有当：在地面 + 按空格 + 动画彻底结束了，才允许跳
        if (Input.GetButtonDown("Jump") && _isGrounded && isJumpAnimationFinished)
        {
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            if (animator != null) animator.SetTrigger("Jump");
        }
    }

    void HandleCameraOrbit()
    {
        if (cameraTransform == null) return;
        _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        _pitch = Mathf.Clamp(_pitch, pitchLimits.x, pitchLimits.y);
    }

    void UpdateCameraPosition()
    {
        if (cameraTransform == null) return;
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
        cameraTransform.rotation = rot;
        cameraTransform.position = transform.position - (rot * Vector3.forward * cameraDistance) + (Vector3.up * 1.5f);
    }

    void HandleAttack()
    {
        if (Input.GetMouseButtonDown(0) && _canAttack)
        {
            StartCoroutine(PerformAttack());
        }
    }

    IEnumerator PerformAttack()
    {
        _canAttack = false;
        if (animator != null) animator.SetTrigger("Attack");

        yield return new WaitForSeconds(0.3f);

        Vector3 pos = transform.position + transform.forward * 1.0f;
        Collider[] hits = Physics.OverlapSphere(pos, attackRange, enemyLayers);
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<Rigidbody>(out Rigidbody rb))
                rb.AddForce((hit.transform.position - transform.position).normalized * 5f, ForceMode.Impulse);
        }

        yield return new WaitForSeconds(attackCooldown);
        _canAttack = true;
    }
}