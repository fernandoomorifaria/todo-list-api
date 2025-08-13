module Todo

open System
open System.Data
open System.Threading.Tasks
open Dapper

type Todo = {
    Id: int
    UserId: int
    Title: string
    CompletedAt: DateTime
}

type CreateTodo = {
    UserId: int
    Title: string
}

type UpdateTodo = {
    Title: string
    (* For some reason it doesn't deserialize well if it's typed as DateTime option *)
    CompletedAt: Nullable<DateTime>
}

type CreateTodoRequest = {
    Title: string
}

type UpdateTodoRequest = {
    Title: string
    CompletedAt: Nullable<DateTime>
}

let getAll (connection: IDbConnection) (userId: int): Task<seq<Todo>> =
    task {
        let sql = """
            select
                id as Id,
                user_id as UserId,
                title as Title,
                completed_at as CompletedAt
            from todo
            where user_id = @userId
        """

        return! connection.QueryAsync<Todo>(sql, {| userId = userId |})
    }

let get (connection: IDbConnection) (id: int): Task<Todo option> =
    task {
        let sql = """
            select
                id as Id,
                user_id as UserId,
                title as Title,
                completed_at as CompletedAt
            from todo
            where id = @id
        """

        let! todo = connection.QuerySingleAsync<Todo>(sql, {| id = id |})

        match box todo with
        | null -> return None
        | _ -> return Some todo
    }

let create (connection: IDbConnection) (todo: CreateTodo) =
    task {
        let sql = "insert into todo (user_id, title) values (@userId, @title) returning id"

        return! connection.ExecuteScalarAsync<int>(sql, {| userId = todo.UserId; title = todo.Title |})
    }

let update (connection: IDbConnection) (id: int) (todo: UpdateTodo) =
    task {
        let sql = "update todo set title = @title, completed_at = @completedAt where id = @id"

        let! _ = connection.ExecuteAsync(sql, {| title = todo.Title; completedAt = todo.CompletedAt |})

        ()
    }

let delete (connection: IDbConnection) (id: int) =
    task {
        let sql = "delete from todo where id = @id"

        let! _ = connection.ExecuteAsync(sql, {| id = id |})

        ()
    }
