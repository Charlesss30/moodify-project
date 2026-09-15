const API_BASE_URL = "/api";

// ==============================
// STATE
// ==============================
let currentUser = null;

// ==============================
// VIEW
// ==============================
const views = {
    login: document.getElementById("login-view"),
    register: document.getElementById("register-view"),
    home: document.getElementById("home-view")
};

function showView(name) {
    Object.entries(views).forEach(([key, element]) => {
        if (element) {
            element.classList.toggle("hidden", key !== name);
        }
    });

    if (name !== "home") {
        const modal = document.getElementById("logout-modal");

        if (modal) {
            modal.classList.add("hidden");
        }
    }

    if (window.lucide) {
        lucide.createIcons();
    }
}

// ==============================
// FIELD ERROR
// ==============================
function setFieldError(inputId, errorId, message) {
    const input = document.getElementById(inputId);
    const error = document.getElementById(errorId);

    if (!input || !error) return;

    const wrap = input.closest(".input-wrap");

    error.textContent = message || "";

    if (wrap) {
        wrap.classList.toggle("error", Boolean(message));
    }
}

function clearLoginErrors() {
    setFieldError(
        "login-identifier",
        "login-identifier-error",
        ""
    );

    setFieldError(
        "login-password",
        "login-password-error",
        ""
    );

    const generalError =
        document.getElementById("login-general-error");

    const success =
        document.getElementById("login-success");

    if (generalError) {
        generalError.classList.add("hidden");
    }

    if (success) {
        success.classList.add("hidden");
    }
}

function clearRegisterErrors() {
    setFieldError(
        "register-name",
        "register-name-error",
        ""
    );

    setFieldError(
        "register-email",
        "register-email-error",
        ""
    );

    setFieldError(
        "register-password",
        "register-password-error",
        ""
    );

    setFieldError(
        "register-confirm",
        "register-confirm-error",
        ""
    );
}

function emailValid(email) {
    return /^\S+@\S+\.\S+$/.test(email);
}

// ==============================
// REGISTER
// ==============================

const agreeTerms =
    document.getElementById("agree-terms");

const registerSubmit =
    document.getElementById("register-submit");

if (agreeTerms && registerSubmit) {

    agreeTerms.addEventListener("change", function () {

        registerSubmit.disabled = !this.checked;

    });
}


const registerForm =
    document.getElementById("register-form");

if (registerForm) {

    registerForm.addEventListener("submit", async function (e) {

        e.preventDefault();

        console.log("Đang đăng ký...");


        const name =
            document.getElementById("register-name").value.trim();

        const email =
            document.getElementById("register-email").value.trim();

        const password =
            document.getElementById("register-password").value;

        const confirmPassword =
            document.getElementById("register-confirm").value;


        // ==============================
        // VALIDATE
        // ==============================

        if (!name) {
            alert("Vui lòng nhập họ tên.");
            return;
        }

        if (!email) {
            alert("Vui lòng nhập email.");
            return;
        }

        if (!emailValid(email)) {
            alert("Email không hợp lệ.");
            return;
        }

        if (!password || password.length < 6) {
            alert("Mật khẩu phải có ít nhất 6 ký tự.");
            return;
        }

        if (password !== confirmPassword) {
            alert("Mật khẩu xác nhận không khớp.");
            return;
        }

        if (!agreeTerms.checked) {
            alert("Vui lòng đồng ý với điều khoản.");
            return;
        }


        // ==============================
        // GỌI API
        // ==============================

        try {

            registerSubmit.disabled = true;
            registerSubmit.textContent = "Đang tạo tài khoản...";


            const response = await fetch(
                `${API_BASE_URL}/Auth/register`,
                {
                    method: "POST",

                    headers: {
                        "Content-Type": "application/json"
                    },

                    body: JSON.stringify({
                        tenDangNhap: name,
                        email: email,
                        matKhau: password
                    })
                }
            );


            const data = await response.json();

            console.log("Register API:", data);


            // ==============================
            // REGISTER FAIL
            // ==============================

            if (!response.ok) {

                alert(
                    data.message ||
                    "Đăng ký thất bại."
                );

                return;
            }


            // ==============================
            // REGISTER SUCCESS
            // ==============================

            const successElement =
                document.getElementById("register-success");

            const successText =
                document.getElementById("register-success-text");


            if (successText) {

                successText.textContent =
                    "Đăng ký thành công! Đang chuyển đến trang đăng nhập...";
            }


            if (successElement) {

                successElement.classList.remove("hidden");
            }


            // Reset form

            registerForm.reset();

            registerSubmit.disabled = true;


            // Chuyển về Login sau 1.5 giây

            setTimeout(() => {

                if (successElement) {
                    successElement.classList.add("hidden");
                }

                showView("login");

                // Điền sẵn email vừa đăng ký

                const loginIdentifier =
                    document.getElementById("login-identifier");

                if (loginIdentifier) {
                    loginIdentifier.value = email;
                }

            }, 1500);


        } catch (error) {

            console.error(
                "Register error:",
                error
            );

            alert(
                "Không thể kết nối đến ASP.NET Core API."
            );

        } finally {

            registerSubmit.disabled =
                !agreeTerms.checked;

            registerSubmit.textContent =
                "Create Account";
        }

    });
}

// ==============================
// LOGIN
// ==============================

const loginForm =
    document.getElementById("login-form");


if (loginForm) {

    loginForm.addEventListener("submit", async function (e) {

        e.preventDefault();

        console.log("Login button được bấm");


        const identifier =
            document
                .getElementById("login-identifier")
                .value
                .trim();

        const password =
            document
                .getElementById("login-password")
                .value;


        const errorElement =
            document.getElementById(
                "login-general-error"
            );

        const successElement =
            document.getElementById(
                "login-success"
            );


        // Xóa thông báo cũ

        if (errorElement) {

            errorElement.textContent = "";

            errorElement.classList.add("hidden");
        }

        if (successElement) {

            successElement.textContent = "";

            successElement.classList.add("hidden");
        }


        // ==============================
        // VALIDATE
        // ==============================

        if (!identifier) {

            if (errorElement) {

                errorElement.textContent =
                    "Vui lòng nhập email hoặc tên đăng nhập.";

                errorElement.classList.remove("hidden");
            }

            return;
        }


        if (!password) {

            if (errorElement) {

                errorElement.textContent =
                    "Vui lòng nhập mật khẩu.";

                errorElement.classList.remove("hidden");
            }

            return;
        }


        // ==============================
        // GỌI API LOGIN
        // ==============================

        try {

            console.log("Đang gọi API Login...");


            const response = await fetch(
                `${API_BASE_URL}/Auth/login`,
                {
                    method: "POST",

                    headers: {
                        "Content-Type": "application/json"
                    },

                    body: JSON.stringify({
                        identifier: identifier,
                        matKhau: password
                    })
                }
            );


            const data = await response.json();

            console.log("Login API:", data);


            // ==============================
            // LOGIN FAIL
            // ==============================

            if (!response.ok) {

                if (errorElement) {

                    errorElement.textContent =
                        data.message ||
                        "Email hoặc mật khẩu không chính xác.";

                    errorElement.classList.remove("hidden");
                }

                return;
            }


            // ==============================
            // LOGIN SUCCESS
            // ==============================

            console.log(
                "Đăng nhập thành công:",
                data.user
            );


            // Lưu user vào biến hiện tại

            currentUser = data.user;


            // Lưu phiên đăng nhập

            localStorage.setItem(
                "moodify_user",
                JSON.stringify(currentUser)
            );

            if (["admin", "quantrivien"].includes(String(currentUser.vaiTro).toLowerCase())) {
                const adminUser = encodeURIComponent(JSON.stringify(currentUser));
                window.location.href = `http://localhost:5174/?adminUser=${adminUser}`;
                return;
            }


            // Nếu sau này dùng JWT
            // localStorage.setItem("access_token", data.token);


            // Hiển thị thông báo

            if (successElement) {

                successElement.textContent =
                    `Đăng nhập thành công! Xin chào ${currentUser.tenDangNhap}.`;

                successElement.classList.remove("hidden");
            }


            // ==============================
            // CẬP NHẬT HOME
            // ==============================

            updateHomeUser();


            // Chờ một chút để người dùng thấy thông báo

            setTimeout(() => {

                showView("home");

            }, 700);


        } catch (error) {

            console.error(
                "Login error:",
                error
            );


            if (errorElement) {

                errorElement.textContent =
                    "Không thể kết nối đến máy chủ.";

                errorElement.classList.remove("hidden");
            }

        }

    });

}

// ==============================
// UPDATE HOME USER
// ==============================

function updateHomeUser() {

    if (!currentUser) {
        return;
    }


    const homeName =
        document.getElementById("home-name");

    const homeRole =
        document.getElementById("home-role");


    if (homeName) {

        homeName.textContent =
            currentUser.tenDangNhap;
    }


    if (homeRole) {

        homeRole.textContent =
            currentUser.vaiTro;
    }


    console.log(
        "Home user:",
        currentUser
    );
}

// ==============================
// PASSWORD SHOW / HIDE
// ==============================

function setupPasswordToggle(
    buttonId,
    inputId
) {

    const button =
        document.getElementById(buttonId);

    const input =
        document.getElementById(inputId);

    if (!button || !input) return;

    button.addEventListener(
        "click",
        function () {

            if (input.type === "password") {

                input.type = "text";

                button.innerHTML =
                    '<i data-lucide="eye-off"></i>';

            } else {

                input.type = "password";

                button.innerHTML =
                    '<i data-lucide="eye"></i>';
            }

            if (window.lucide) {

                lucide.createIcons();
            }
        }
    );
}

setupPasswordToggle(
    "login-toggle-password",
    "login-password"
);

setupPasswordToggle(
    "register-toggle-password",
    "register-password"
);

setupPasswordToggle(
    "register-toggle-confirm",
    "register-confirm"
);

// ==============================
// PAGE NAVIGATION
// ==============================

document
    .querySelectorAll("[data-nav]")
    .forEach(function (button) {

        button.addEventListener(
            "click",
            function () {

                showView(
                    button.dataset.nav
                );
            }
        );
    });

// ==============================
// BOTTOM NAVIGATION
// ==============================

document
    .querySelectorAll(".nav-item")
    .forEach(function (button) {

        button.addEventListener(
            "click",
            function () {

                document
                    .querySelectorAll(".nav-item")
                    .forEach(function (item) {

                        item.classList.remove(
                            "active"
                        );
                    });

                button.classList.add("active");

                const tab =
                    button.dataset.tab;

                const result =
                    document.getElementById(
                        "mood-result"
                    );

                if (!result) return;

                if (tab === "home") {

                    result.classList.add(
                        "hidden"
                    );

                    return;
                }

                result.textContent =
                    `${tab} đang chờ kết nối Backend/API.`;

                result.classList.remove(
                    "hidden"
                );
            }
        );
    });

// ==============================
// MOOD ANALYSIS
// ==============================

const analyzeButton =
    document.getElementById(
        "analyze-btn"
    );

if (analyzeButton) {

    analyzeButton.addEventListener(
        "click",
        async function () {

            const input =
                document.getElementById(
                    "mood-input"
                );

            const result =
                document.getElementById(
                    "mood-result"
                );

            if (!input || !result) return;

            const text =
                input.value.trim();

            if (!text) {

                result.textContent =
                    "Vui lòng nhập mô tả tâm trạng.";

                result.classList.remove(
                    "hidden"
                );

                return;
            }

            // ==========================================
            // SAU NÀY KẾT NỐI BACKEND
            // ==========================================
            //
            // POST /api/mood/analyze
            //
            // Body:
            //
            // {
            //     "vanBanDauVao": text
            // }
            //
            // ==========================================

            result.textContent =
                "Đã nhận nội dung tâm trạng. Phần phân tích AI sẽ được kết nối với ASP.NET Core API.";

            result.classList.remove(
                "hidden"
            );
        }
    );
}

// ==============================
// LOGOUT
// ==============================

const logoutButton =
    document.getElementById(
        "logout-btn"
    );

const cancelLogout =
    document.getElementById(
        "cancel-logout"
    );

const confirmLogout =
    document.getElementById(
        "confirm-logout"
    );

if (logoutButton) {

    logoutButton.addEventListener(
        "click",
        function () {

            const modal =
                document.getElementById(
                    "logout-modal"
                );

            if (modal) {

                modal.classList.remove(
                    "hidden"
                );
            }
        }
    );
}

if (cancelLogout) {

    cancelLogout.addEventListener(
        "click",
        function () {

            const modal =
                document.getElementById(
                    "logout-modal"
                );

            if (modal) {

                modal.classList.add(
                    "hidden"
                );
            }
        }
    );
}

if (confirmLogout) {

    confirmLogout.addEventListener(
        "click",
        function () {

            currentUser = null;

            // Xóa phiên đăng nhập
            localStorage.removeItem("moodify_user");

            // Sau này khi có JWT
            localStorage.removeItem("access_token");

            const modal =
                document.getElementById(
                    "logout-modal"
                );

            if (modal) {

                modal.classList.add(
                    "hidden"
                );
            }

            showView("login");
        }
    );
}

// ==============================
// INITIALIZE
// ==============================

showView("login");