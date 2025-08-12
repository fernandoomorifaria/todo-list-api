module User

open System.Data
open System.Threading.Tasks
open Dapper

type User = {
    Id: int
    Email: string
}

let get (connection: IDbConnection) (emailAddress: string): Task<User option> =
    task {
        let sql = "select id as Id, email as Email from \"user\" where email = @email"

        let! user = connection.QuerySingleOrDefaultAsync<User>(sql, {| email = emailAddress |})

        match box user with
        | null -> return None
        | _ -> return Some user
    }


let create (connection: IDbConnection) (emailAddress: string) =
    task {
        let sql = "insert into \"user\" (email) values (@email) returning id"

        let! id = connection.QuerySingleAsync<int>(sql, {| email = emailAddress |})

        return {
            Id = id;
            Email = emailAddress
        }
    }

let getOrCreate (connection: IDbConnection) (emailAddress: string) =
    task {
        let! user = get connection emailAddress 

        match user with
        | Some user -> return user
        | None -> return! create connection emailAddress
    }
