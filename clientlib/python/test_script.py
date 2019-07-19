
c = Client('test_user')

c.api_url = 'http://localhost:8080/api/'
game_response = c.create_game()

if c.update_game():
    print("Updated the game")
