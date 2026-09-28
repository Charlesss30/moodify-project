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

        console.log("Registering...");


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
            alert("Please enter your name.");
            return;
        }

        if (!email) {
            alert("Please enter your email.");
            return;
        }

        if (!emailValid(email)) {
            alert("Invalid email address.");
            return;
        }

        if (!password || password.length < 6) {
            alert("Password must have at least 6 characters.");
            return;
        }

        if (password !== confirmPassword) {
            alert("Passwords do not match.");
            return;
        }

        if (!agreeTerms.checked) {
            alert("Please accept the terms.");
            return;
        }


        // ==============================
        // GỌI API
        // ==============================

        try {

            registerSubmit.disabled = true;
            registerSubmit.textContent = "Creating your account...";


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
                    "Registration failed."
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
                    "Registration successful! Redirecting to login...";
            }


            if (successElement) {

                successElement.classList.remove("hidden");
            }


            // Reset form

            registerForm.reset();

            registerSubmit.disabled = true;


            // Chuyển về Login sau 1.5 seconds

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
                "Unable to connect to the API."
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


        // Delete thông báo cũ

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
                    "Please enter your email or username.";

                errorElement.classList.remove("hidden");
            }

            return;
        }


        if (!password) {

            if (errorElement) {

                errorElement.textContent =
                    "Please enter your password.";

                errorElement.classList.remove("hidden");
            }

            return;
        }


        // ==============================
        // GỌI API LOGIN
        // ==============================

        try {

            console.log("Signing in...");


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
                        "Incorrect email or password.";

                    errorElement.classList.remove("hidden");
                }

                return;
            }


            // ==============================
            // LOGIN SUCCESS
            // ==============================

            console.log(
                "Login successful:",
                data.user
            );


            // Save user vào biến hiện tại

            currentUser = data.user;
            sessionStorage.setItem("moodify_user_token", data.token);


            // Save phiên đăng nhập

            localStorage.setItem(
                "moodify_user",
                JSON.stringify(currentUser)
            );

            if (["admin", "quantrivien"].includes(String(currentUser.vaiTro).toLowerCase())) {
                const adminUrl = new URL(window.location.origin);
                adminUrl.port = '5174';
                window.location.replace(adminUrl.href);
                return;
            }


            // Nếu sau này dùng JWT
            // localStorage.setItem("access_token", data.token);


            // Hiển thị thông báo

            if (successElement) {

                successElement.textContent =
                    `Login successful! Hello ${currentUser.tenDangNhap}.`;

                successElement.classList.remove("hidden");
            }


            // ==============================
            // CẬP NHẬT HOME
            // ==============================

            updateHomeUser();


            // Chờ một chút để người dùng thấy thông báo

            setTimeout(() => {

                showView("home");
                window.dispatchEvent(new Event("moodify-user-login"));

            }, 700);


        } catch (error) {

            console.error(
                "Login error:",
                error
            );


            if (errorElement) {

                errorElement.textContent =
                    "Unable to connect to the server.";

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
                    `${tab} is awaiting the backend connection.`;

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
                    "Please describe your mood.";

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
                "Your mood description has been received. AI analysis is not connected yet.";

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
            sessionStorage.removeItem("moodify_user_token");
            window.dispatchEvent(new Event("moodify-user-logout"));

            // Delete phiên đăng nhập
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