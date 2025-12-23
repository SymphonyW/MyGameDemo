using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI; // 引入UI命名空间

/// <summary>
/// 第三人称近战 Demo 控制器 (最终修复版 + 固定格子背包系统)
/// 修复：合并 Move 调用，彻底解决 CharacterController 落地判定失效的问题
/// 新增：UI 优化，支持文字隐藏逻辑
/// 修复：解决背包打开时角色悬空的问题 (保留重力计算)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class MeleeDemoController : MonoBehaviour
{
    [Header("核心组件")]
    public Animator animator;

    [Header("UI设置")]
    [Tooltip("背包的面板对象 (Panel)")]
    public GameObject inventoryUI;

    [Tooltip("物品格子的预制体 (建议结构：父物体Empty -> 子物体Image(上) + 子物体Text(下))")]
    public GameObject itemSlotPrefab;

    [Tooltip("格子的父容器 (请在 UI 里创建一个 Panel，添加 Grid Layout Group 组件，然后拖进来)")]
    public Transform inventoryGridParent;

    [Tooltip("背包最大容量 (格子数量)")]
    public int inventoryCapacity = 20;

    private bool _isInventoryOpen = false;

    // 简单的物品数据
    private List<string> _myItems = new List<string>();

    [Header("角色属性")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 8f;
    public float jumpHeight = 1.5f;
    public float gravity = -20.0f;
    public float turnSmoothTime = 0.1f;

    [Header("地面检测优化")]
    public float jumpToleranceTime = 0.2f;
    private float _lastGroundedTime;

    [Header("战斗设置")]
    public float attackRange = 1.5f;
    public float attackCooldown = 0.5f;
    public LayerMask enemyLayers;
    public float comboResetTime = 1.0f;

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

    // 连招变量
    private int _comboIndex = 0;
    private float _lastAttackTime = 0f;

    void Start()
    {
        _controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        // --- 初始化背包数据 ---
        if (inventoryUI != null) inventoryUI.SetActive(false);
        // 添加一些测试物品
        _myItems.Add("HP 药水");
        _myItems.Add("铁剑");
        _myItems.Add("苹果");
        _myItems.Add("钥匙");
        _myItems.Add("盾牌");
        _myItems.Add("魔法书");
        _myItems.Add("金币");

        UpdateCursorState();

        Vector3 angles = transform.eulerAngles;
        _yaw = angles.y;
    }

    void Update()
    {
        // 1. 获取地面状态
        _isGrounded = _controller.isGrounded;

        if (_isGrounded)
        {
            _lastGroundedTime = Time.time;

            // 地面吸附
            if (_velocity.y < 0)
            {
                _velocity.y = -5f;
            }
        }

        if (animator != null) animator.SetBool("IsGrounded", _isGrounded);

        // --- 背包逻辑 ---
        if (Input.GetKeyDown(KeyCode.E)) ToggleInventory();

        // 2. 逻辑分支：背包打开 vs 正常游戏
        if (_isInventoryOpen)
        {
            // --- 背包打开状态 ---
            // 停止跑步动画
            if (animator != null) animator.SetFloat("Speed", 0f);

            // 强制将水平速度归零 (防止滑行)
            _velocity.x = 0f;
            _velocity.z = 0f;

            // 注意：这里不再 return，而是继续向下执行重力逻辑
        }
        else
        {
            // --- 正常游戏状态 ---
            // 只有背包关闭时，才响应玩家操作
            HandleCameraOrbit();
            HandleMovement();
            HandleAttack();
        }

        // 3. 应用重力 (始终执行，防止悬空)
        _velocity.y += gravity * Time.deltaTime;

        // 4. 统一执行 Move (始终执行)
        _controller.Move(_velocity * Time.deltaTime);
    }

    void LateUpdate()
    {
        // 【核心修复】移除 if (!_isInventoryOpen) 判断
        // 始终更新相机位置，这样即使打开背包导致角色下落，相机也会跟着下移
        // (注：因为 HandleCameraOrbit 在 Update 里被限制了，所以鼠标动的时候视角不会转，只会跟随位置，这是对的)
        UpdateCameraPosition();
    }

    void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 dir = new Vector3(h, 0f, v).normalized;

        // --- 水平移动计算 ---
        if (dir.magnitude >= 0.1f)
        {
            bool isSprinting = Input.GetKey(KeyCode.LeftShift);
            float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;

            float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            Vector3 finalMove = moveDir.normalized * currentSpeed;
            _velocity.x = finalMove.x;
            _velocity.z = finalMove.z;

            if (animator != null) animator.SetFloat("Speed", isSprinting ? 1f : 0.5f, 0.1f, Time.deltaTime);
        }
        else
        {
            _velocity.x = 0f;
            _velocity.z = 0f;

            if (animator != null) animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
        }

        // --- 跳跃逻辑 ---
        bool canJump = true;
        bool isGroundedEffective = (Time.time - _lastGroundedTime <= jumpToleranceTime);

        if (animator != null)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName("Jump") && !isGroundedEffective)
            {
                canJump = false;
            }
        }

        if (Input.GetButtonDown("Jump") && isGroundedEffective && canJump)
        {
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _lastGroundedTime = -100f;

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

    void ToggleInventory()
    {
        if (inventoryUI == null) { Debug.LogWarning("UI 未设置"); return; }

        _isInventoryOpen = !_isInventoryOpen;
        inventoryUI.SetActive(_isInventoryOpen);

        if (_isInventoryOpen)
        {
            RefreshInventoryDisplay();
        }

        UpdateCursorState();
    }

    // 【修改】优化了文字显示逻辑
    void RefreshInventoryDisplay()
    {
        if (inventoryGridParent == null || itemSlotPrefab == null)
        {
            Debug.LogWarning("请在 Inspector 中设置 ItemSlotPrefab 和 InventoryGridParent！");
            return;
        }

        // 1. 清空旧格子
        foreach (Transform child in inventoryGridParent)
        {
            Destroy(child.gameObject);
        }

        // 2. 生成固定数量的格子
        for (int i = 0; i < inventoryCapacity; i++)
        {
            GameObject newSlot = Instantiate(itemSlotPrefab, inventoryGridParent);

            // 尝试获取 Text 组件 (无论它在预制体的哪一层)
            Text slotText = newSlot.GetComponentInChildren<Text>();
            Image slotImage = newSlot.GetComponent<Image>();

            // 获取背景 Image (如果预制体根节点没有Image，尝试找子节点里的Image)
            // 注意：如果根节点是 Empty，GetComponent<Image> 会返回 null，这是预期的
            if (slotImage == null) slotImage = newSlot.GetComponentInChildren<Image>();

            if (i < _myItems.Count)
            {
                // --- 有物品 ---
                if (slotText != null)
                {
                    slotText.gameObject.SetActive(true); // 确保文字开启
                    slotText.text = _myItems[i];
                    slotText.color = Color.black;
                }

                // 物品格高亮 (白色不透明)
                if (slotImage != null)
                {
                    slotImage.color = new Color(1f, 1f, 1f, 1f);
                }
            }
            else
            {
                // --- 空格子 ---
                if (slotText != null)
                {
                    // 直接隐藏文字组件，防止占位或显示乱码
                    slotText.gameObject.SetActive(false);
                }

                // 空格半透明
                if (slotImage != null)
                {
                    slotImage.color = new Color(1f, 1f, 1f, 0.5f);
                }
            }
        }
    }

    void UpdateCursorState()
    {
        if (_isInventoryOpen) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    void HandleAttack()
    {
        if (Input.GetMouseButtonDown(0) && _canAttack)
        {
            if (Time.time - _lastAttackTime > comboResetTime) _comboIndex = 0;
            StartCoroutine(PerformAttack());
        }
    }

    IEnumerator PerformAttack()
    {
        _canAttack = false;
        if (animator != null)
        {
            animator.SetInteger("AttackIndex", _comboIndex);
            animator.SetTrigger("Attack");
            _comboIndex = (_comboIndex + 1) % 2;
        }

        yield return new WaitForSeconds(0.3f);

        Vector3 pos = transform.position + transform.forward * 1.0f;
        Collider[] hits = Physics.OverlapSphere(pos, attackRange, enemyLayers);
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<Rigidbody>(out Rigidbody rb))
                rb.AddForce((hit.transform.position - transform.position).normalized * 5f, ForceMode.Impulse);
        }

        yield return new WaitForSeconds(attackCooldown);
        _lastAttackTime = Time.time;
        _canAttack = true;
    }
}