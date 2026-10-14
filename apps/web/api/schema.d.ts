/** Generated from OpenAPI. Do not edit manually. */
export interface paths {
    "/api/v1/auth/csrf": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CsrfDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/register": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["Credentials"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SessionDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["Credentials"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SessionDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/session": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SessionDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/logout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfOrganizationDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["OrganizationInput"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["OrganizationDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/members": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                };
                header?: never;
                path: {
                    organizationId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfMemberDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["MemberInput"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemberDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/members/{userId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/workspace": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["WorkspaceDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/internal/v1/executions/{id}/repository-token": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RepositoryTokenInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CheckoutGrant"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                };
                header?: never;
                path: {
                    organizationId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfProjectDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ProjectInput"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProjectDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProjectDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ProjectUpdate"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProjectDto"];
                    };
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query: {
                    version: string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfTaskDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["TaskInput"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TaskDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TaskDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["TaskUpdate"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TaskDto"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/transition": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["TransitionInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TaskDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/sequence/start": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TaskDto"][];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/sequence/stop": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TaskDto"][];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/skills": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SkillInfo"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfExecutionDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: {
                    "Idempotency-Key"?: string;
                };
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ExecutionInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExecutionDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/cancel": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExecutionDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/progress": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    after?: string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExecutionProgress"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/report": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/reports/{eventId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                    eventId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/events": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/publication": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PublicationStatus"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PublicationStatus"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/review": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ReviewStatus"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/review/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ReviewStatus"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/review/refresh": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ReviewStatus"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/review/merge": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ReviewStatus"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/executions/{executionId}/review/complete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    taskId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ReviewStatus"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                    archived?: boolean;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfMemoryDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["MemoryInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemoryDetail"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory/{id}/indexes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfIndexDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["IndexInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["IndexDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory/search": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SearchInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ContextBundle"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory/analyses": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AnalysisInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnalysisDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory/analyses/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnalysisDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory/from-execution/{executionId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemoryDetail"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemoryDetail"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["MemoryUpdate"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemoryDetail"];
                    };
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query: {
                    version: string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemoryDetail"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory/{id}/restore": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["MemoryVersionInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemoryDetail"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/memory/{id}/versions/{version}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                    version: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemoryVersionDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/runners": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfRunnerDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RunnerInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["IssuedRunner"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/runners/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunnerStatusDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/runners/{id}/rotate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RunnerInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["IssuedRunner"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/runners/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/internal/v1/executions/claim": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ClaimInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunnerMessage"];
                    };
                };
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/internal/v1/executions/{id}/heartbeat": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["HeartbeatInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["HeartbeatReply"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["HeartbeatReply"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/internal/v1/executions/{id}/checkpoint": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CheckpointInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CheckpointReply"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/internal/v1/executions/{id}/checkpoint/read": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CheckpointReadInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CheckpointReply"];
                    };
                };
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/internal/v1/executions/{id}/recovery": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RecoveryInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RecoveryReply"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/internal/v1/executions/{id}/model-turn": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ModelTurnInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ModelTurnReply"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/internal/v1/executions/{id}/result": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ExecutionResultInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExecutionResultReply"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/repository/local/directories": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    path?: string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LocalDirectoryListing"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/repository/local/inspect": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["LocalFolderInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LocalFolderInspection"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/repository/local/folder": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["LocalFolderInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["GitDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/repository/local": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["LocalRepositoryInput"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["GitDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/repository": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["GitDto"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/repository/local/reviews/{executionId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    executionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/profiles/agents": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CliAgent"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/profiles/machine": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CodexMachineStatus"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProfileDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/profiles/codex": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/profiles": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                };
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PageResultOfProfileDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/organizations/{organizationId}/projects/{projectId}/profiles/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    organizationId: string;
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        AgentChoice: {
            /** @default codex */
            provider: string;
            /** @default default */
            model: string;
            effort?: null | string;
        };
        AgentToolPolicy: {
            readable: string[];
            editable: string[];
            create?: null | string[];
        };
        AnalysisDto: {
            /** Format: uuid */
            id: string;
            state: string;
            failureCode: null | string;
            plan: null | components["schemas"]["PlanDraft"];
            context: null | components["schemas"]["ContextBundle"];
            /** Format: double */
            maxCostUsd: number | string;
            /** Format: double */
            reservedUsd: number | string;
            /** Format: double */
            estimatedCostUsd: null | number | string;
            /** Format: int32 */
            inputTokens: null | number | string;
            /** Format: int32 */
            outputTokens: null | number | string;
            /** Format: date-time */
            createdAt: string;
        };
        AnalysisInput: {
            demand: string;
            expectedBehavior: null | string;
            /** Format: uuid */
            profileId: string;
            idempotencyKey: string;
            /**
             * Format: double
             * @default 1
             */
            maxCostUsd: number | string;
        };
        CheckoutGrant: {
            token: string;
            /** Format: date-time */
            expiresAt: string;
        };
        CheckpointContext: {
            /** Format: int32 */
            schemaVersion: number | string;
            /** Format: uuid */
            executionId: string;
            objective: string;
            acceptanceCriteria: string[];
            baseCommit: string;
            evidence: components["schemas"]["CheckpointEvidence"][];
            pending: string[];
            memory?: unknown;
            history?: unknown;
        };
        CheckpointEvidence: {
            /** Format: int32 */
            step: number | string;
            tool: string;
            path: null | string;
            succeeded: boolean;
            detail: string;
            hash?: null | string;
        };
        CheckpointInput: {
            /** Format: uuid */
            leaseToken: string;
            context: components["schemas"]["CheckpointContext"];
        };
        CheckpointReadInput: {
            /** Format: uuid */
            leaseToken: string;
        };
        CheckpointReply: {
            /** Format: int32 */
            attempt: number | string;
            /** Format: int32 */
            sequence: number | string;
            contentHash: string;
            /** Format: date-time */
            createdAt: string;
            context: components["schemas"]["CheckpointContext"];
        };
        ClaimInput: {
            /**
             * Format: int32
             * @default 30
             */
            leaseSeconds: number | string;
        };
        CliAgent: {
            id: string;
            name: string;
            installed: boolean;
            authenticated: boolean;
            enabled: boolean;
            models: components["schemas"]["CliModel"][];
        };
        CliModel: {
            id: string;
            efforts: string[];
        };
        CodexMachineStatus: {
            installed: boolean;
            authenticated: boolean;
            enabled: boolean;
        };
        ContextBundle: {
            /** Format: int32 */
            schemaVersion: number | string;
            query: string;
            sources: components["schemas"]["ContextSource"][];
            /** Format: int32 */
            bytes: number | string;
            warnings: string[];
        };
        ContextSource: {
            /** Format: uuid */
            itemId: string;
            /** Format: uuid */
            version: string;
            /** Format: uuid */
            indexId: string;
            /** Format: int32 */
            sequence: number | string;
            key: string;
            title: string;
            content: string;
            contentHash: string;
            sourceHash: string;
            commit: null | string;
            pullRequestUrl: null | string;
        };
        Credentials: {
            email: string;
            password: string;
        };
        CsrfDto: {
            token: string;
        };
        ExecutionDto: {
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            taskId: string;
            state: components["schemas"]["ExecutionState"];
            /** Format: int32 */
            attempt: number | string;
            /** Format: int32 */
            maxAttempts: number | string;
            /** Format: int32 */
            maxDurationSeconds: number | string;
            /** Format: int32 */
            maxSteps: number | string;
            /** Format: double */
            maxCostUsd: number | string;
            /** Format: date-time */
            createdAt: string;
            cancellationRequested: boolean;
            failureCode: null | string;
            /** Format: uuid */
            parentExecutionId?: null | string;
            skill?: null | string;
        };
        ExecutionInput: {
            /** Format: uuid */
            version: string;
            /**
             * Format: int32
             * @default 900
             */
            maxDurationSeconds: number | string;
            /**
             * Format: int32
             * @default 3
             */
            maxAttempts: number | string;
            /**
             * Format: int32
             * @default 50
             */
            maxSteps: number | string;
            /**
             * Format: double
             * @default 5
             */
            maxCostUsd: number | string;
            /** Format: uuid */
            agentProfileId?: null | string;
            /** Format: uuid */
            parentExecutionId?: null | string;
            feedback?: null | string;
            skill?: null | string;
        };
        ExecutionProgress: {
            execution: components["schemas"]["ExecutionDto"];
            /** Format: double */
            estimatedCostUsd: null | number | string;
            /** Format: int32 */
            inputTokens: null | number | string;
            /** Format: int32 */
            outputTokens: null | number | string;
            events: components["schemas"]["ProgressEvent"][];
            nextCursor: null | string;
            agentSummary?: null | string;
            /** Format: int32 */
            cachedInputTokens?: null | number | string;
        };
        ExecutionResultInput: {
            /** Format: uuid */
            leaseToken: string;
            /** Format: uuid */
            operationId: string;
            /** Format: int32 */
            checkpointSequence: number | string;
            validated: boolean;
            reason: string;
        };
        ExecutionResultReply: {
            /** Format: uuid */
            executionId: string;
            /** Format: uuid */
            operationId: string;
            validated: boolean;
            reason: string;
            /** Format: int32 */
            checkpointSequence: number | string;
        };
        /** @enum {unknown} */
        ExecutionState: "queued" | "leased" | "succeeded" | "failed" | "cancelled";
        GitDto: {
            /** Format: uuid */
            id: string;
            provider: string;
            /** Format: int64 */
            repositoryId: number | string;
            remoteUrl: string;
            baseBranch: string;
            baseCommit: string;
            /** Format: uuid */
            version: string;
            /** Format: date-time */
            revokedAt: null | string;
        };
        HeartbeatInput: {
            /** Format: uuid */
            leaseToken: string;
            /**
             * Format: int32
             * @default 30
             */
            leaseSeconds: number | string;
        };
        HeartbeatReply: {
            continueExecution: boolean;
            code?: null | string;
        };
        IndexDto: {
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            itemId: string;
            /** Format: uuid */
            sourceVersion: string;
            model: string;
            /** Format: int32 */
            dimensions: number | string;
            chunker: string;
            state: string;
            failureCode: null | string;
            /** Format: date-time */
            createdAt: string;
            /** Format: date-time */
            completedAt: null | string;
            /** Format: double */
            maxCostUsd: number | string;
            /** Format: double */
            reservedUsd: number | string;
            /** Format: double */
            estimatedCostUsd: null | number | string;
            /** Format: int32 */
            inputTokens: null | number | string;
        };
        IndexInput: {
            /** Format: uuid */
            version: string;
            /** Format: uuid */
            profileId: string;
            idempotencyKey: string;
            /** Format: double */
            maxCostUsd: number | string;
        };
        IssuedRunner: {
            credential: components["schemas"]["RunnerDto"];
            token: string;
        };
        JsonElement: unknown;
        LocalDirectoryEntry: {
            name: string;
            path: string;
        };
        LocalDirectoryListing: {
            currentPath: null | string;
            parentPath: null | string;
            roots: string[];
            directories: components["schemas"]["LocalDirectoryEntry"][];
            isRepository: boolean;
            canConnect: boolean;
            truncated: boolean;
        };
        LocalFolderInput: {
            path: string;
            baseBranch?: null | string;
            /** Format: uuid */
            version?: null | string;
        };
        LocalFolderInspection: {
            path: string;
            hasGit: boolean;
            branches: string[];
            currentBranch: null | string;
        };
        LocalRepositoryInput: {
            path: string;
            baseBranch: string;
        };
        MemberDto: {
            /** Format: uuid */
            userId: string;
            email: string;
            role: components["schemas"]["MemberRole"];
        };
        MemberInput: {
            email: string;
        };
        MemoryDetail: {
            item: components["schemas"]["MemoryDto"];
            content: string;
        };
        MemoryDto: {
            /** Format: uuid */
            id: string;
            key: string;
            title: string;
            kind: components["schemas"]["MemoryKind"];
            status: components["schemas"]["MemoryStatus"];
            /** Format: uuid */
            version: string;
            contentHash: string;
            /** Format: uuid */
            createdBy: string;
            /** Format: uuid */
            updatedBy: string;
            /** Format: date-time */
            createdAt: string;
            /** Format: date-time */
            updatedAt: string;
            /** Format: date-time */
            archivedAt: null | string;
            /** Format: uuid */
            taskId: null | string;
            /** Format: uuid */
            executionId: null | string;
            pullRequestUrl: null | string;
            commit: null | string;
        };
        MemoryInput: {
            key: string;
            title: string;
            content: string;
            kind?: components["schemas"]["MemoryKind"];
        };
        /** @enum {unknown} */
        MemoryKind: "document" | "code" | "decision" | "resolution";
        /** @enum {unknown} */
        MemoryStatus: "candidate" | "validated" | "obsolete";
        MemoryUpdate: {
            /** Format: uuid */
            version: string;
            title: string;
            content: string;
            status: components["schemas"]["MemoryStatus"];
        };
        MemoryVersionDto: {
            /** Format: uuid */
            version: string;
            title: string;
            content: string;
            contentHash: string;
            status: components["schemas"]["MemoryStatus"];
            archived: boolean;
            /** Format: uuid */
            authorId: string;
            /** Format: date-time */
            createdAt: string;
        };
        MemoryVersionInput: {
            /** Format: uuid */
            version: string;
        };
        ModelAction: {
            tool: string;
            path?: null | string;
            content?: null | string;
            expectedHash?: null | string;
        };
        ModelTurnInput: {
            /** Format: uuid */
            leaseToken: string;
            /** Format: uuid */
            operationId: string;
            /** Format: int32 */
            sequence: number | string;
            policy: components["schemas"]["AgentToolPolicy"];
        };
        ModelTurnReply: {
            /** Format: uuid */
            operationId: string;
            action: components["schemas"]["ModelAction"];
            /** Format: double */
            estimatedCostUsd: null | number | string;
            /** Format: int32 */
            inputTokens: null | number | string;
            /** Format: int32 */
            outputTokens: null | number | string;
        };
        OrganizationDto: {
            /** Format: uuid */
            id: string;
            name: string;
            role: components["schemas"]["MemberRole"];
        };
        OrganizationInput: {
            name: string;
        };
        PageResultOfExecutionDto: {
            items: components["schemas"]["ExecutionDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PageResultOfIndexDto: {
            items: components["schemas"]["IndexDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PageResultOfMemberDto: {
            items: components["schemas"]["MemberDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PageResultOfMemoryDto: {
            items: components["schemas"]["MemoryDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PageResultOfOrganizationDto: {
            items: components["schemas"]["OrganizationDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PageResultOfProfileDto: {
            items: components["schemas"]["ProfileDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PageResultOfProjectDto: {
            items: components["schemas"]["ProjectDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PageResultOfRunnerDto: {
            items: components["schemas"]["RunnerDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PageResultOfTaskDto: {
            items: components["schemas"]["TaskDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PlanDraft: {
            title: string;
            description: string;
            acceptanceCriteria: string[];
            questions: string[];
        };
        ProfileDto: {
            /** Format: uuid */
            id: string;
            name: string;
            provider: string;
            model: string;
            capabilities: string[];
            tools: string[];
            /** Format: int32 */
            maxInputBytes: number | string;
            /** Format: int32 */
            maxOutputTokens: number | string;
            /** Format: int32 */
            timeoutSeconds: number | string;
            /** Format: double */
            inputUsdPerMillionTokens: number | string;
            /** Format: double */
            outputUsdPerMillionTokens: number | string;
            /** Format: date-time */
            revokedAt: null | string;
        };
        ProgressEvent: {
            eventId: string;
            /** Format: uuid */
            executionId: string;
            /** Format: int64 */
            sequence: number | string;
            /** Format: int32 */
            version: number | string;
            /** Format: date-time */
            timestamp: string;
            /** Format: int32 */
            attempt: number | string;
            kind: string;
            tool: null | string;
            path: null | string;
            succeeded: null | boolean;
            hash: null | string;
        };
        ProjectDto: {
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            organizationId: string;
            name: string;
            /** Format: uuid */
            version: string;
        };
        ProjectInput: {
            name: string;
        };
        ProjectUpdate: {
            name: string;
            /** Format: uuid */
            version: string;
        };
        PublicationStatus: {
            state: string;
            /** Format: int32 */
            attempts: number | string;
            failureCode: null | string;
            pullRequest: null | components["schemas"]["PublishedPullRequest"];
        };
        PublishedPullRequest: {
            /** Format: int64 */
            number: number | string;
            url: string;
            baseCommit: string;
            headCommit: string;
            branch: string;
        };
        RecoveryInput: {
            /** Format: uuid */
            leaseToken: string;
        };
        RecoveryReply: {
            checkpoint: null | components["schemas"]["CheckpointReply"];
            writes: components["schemas"]["RecoveryWrite"][];
        };
        RecoveryWrite: {
            /** Format: int32 */
            step: number | string;
            path: string;
            content: string;
            expectedHash: string;
            hash: string;
        };
        RepositoryTokenInput: {
            /** Format: uuid */
            leaseToken: string;
        };
        ReviewStatus: {
            state: string;
            headCommit: null | string;
            ciPassed: boolean;
            /** Format: uuid */
            approvedBy: null | string;
            mergeCommit: null | string;
            /** @default false */
            mergeAvailable: boolean;
        };
        RunnerDto: {
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            workerId: string;
            /** Format: date-time */
            expiresAt: string;
            /** Format: date-time */
            revokedAt: null | string;
        };
        RunnerInput: {
            /**
             * Format: int32
             * @default 7
             */
            validityDays: number | string;
        };
        RunnerLimits: {
            /** Format: int32 */
            maxDurationSeconds: number | string;
            /** Format: int32 */
            maxAttempts: number | string;
            /** Format: int32 */
            maxSteps: number | string;
            /** Format: double */
            maxCostUsd: number | string;
        };
        RunnerMessage: {
            /** Format: int32 */
            schemaVersion: number | string;
            /** Format: uuid */
            executionId: string;
            /** Format: uuid */
            taskId: string;
            /** Format: uuid */
            organizationId: string;
            /** Format: uuid */
            projectId: string;
            /** Format: uuid */
            workerId: string;
            /** Format: uuid */
            leaseToken: string;
            /** Format: date-time */
            leaseExpiresAt: string;
            /** Format: date-time */
            deadlineAt: string;
            /** Format: int32 */
            attempt: number | string;
            snapshot: components["schemas"]["JsonElement"];
            limits: components["schemas"]["RunnerLimits"];
            repository: unknown;
            /** Format: uuid */
            agentProfileId: null | string;
            memory?: unknown;
            transfer?: unknown;
        };
        RunnerStatusDto: {
            configured: boolean;
            online: boolean;
            /** Format: date-time */
            lastSeenAt: null | string;
        };
        SearchInput: {
            query: string;
            /**
             * Format: int32
             * @default 8192
             */
            maxBytes: number | string;
        };
        SessionDto: {
            /** Format: uuid */
            userId: string;
            email: string;
        };
        SkillInfo: {
            id: string;
            name: string;
            description: string;
            source: string;
            /** Format: int32 */
            bytes: number | string;
            preview: string;
        };
        TaskDto: {
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            organizationId: string;
            /** Format: uuid */
            projectId: string;
            title: string;
            description: string;
            acceptanceCriteria: string[];
            /** Format: uuid */
            assigneeId: null | string;
            dependencyIds: string[];
            stage: components["schemas"]["TaskStage"];
            /** Format: uuid */
            version: string;
            /** Format: date-time */
            createdAt: string;
            allowedTransitions: components["schemas"]["TaskStage"][];
            /** Format: uuid */
            analysisId?: null | string;
            folderPath?: null | string;
            baseBranch?: null | string;
            /** Format: uuid */
            agentProfileId?: null | string;
            kind?: components["schemas"]["TaskKind"];
            /** Format: uuid */
            previousTaskId?: null | string;
            /** @default false */
            sequenceRunning: boolean;
        };
        TaskInput: {
            title: string;
            description: string;
            acceptanceCriteria: string[];
            /** Format: uuid */
            assigneeId: null | string;
            dependencyIds: string[];
            /** Format: uuid */
            analysisId?: null | string;
            folderPath?: null | string;
            baseBranch?: null | string;
            stage?: null | components["schemas"]["TaskStage"];
            ai?: null | components["schemas"]["AgentChoice"];
            kind?: null | components["schemas"]["TaskKind"];
            /** Format: uuid */
            previousTaskId?: null | string;
        };
        /** @enum {unknown} */
        TaskStage: "intake" | "analysis" | "ready" | "execution" | "review" | "awaitingMerge" | "done" | "blocked" | "failed" | "cancelled";
        TaskUpdate: {
            title: string;
            description: string;
            acceptanceCriteria: string[];
            /** Format: uuid */
            assigneeId: null | string;
            dependencyIds: string[];
            /** Format: uuid */
            version: string;
        };
        TransitionInput: {
            stage: components["schemas"]["TaskStage"];
            /** Format: uuid */
            version: string;
        };
        WorkspaceDto: {
            /** Format: uuid */
            organizationId: string;
            /** Format: uuid */
            projectId: string;
        };
        /** @enum {unknown} */
        MemberRole: "member" | "owner";
        /**
         * @default change
         * @enum {unknown}
         */
        TaskKind: "change" | "analysis";
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export type operations = Record<string, never>;
